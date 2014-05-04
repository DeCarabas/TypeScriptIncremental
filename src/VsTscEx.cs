namespace TypeScript.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;
    using Microsoft.Build.Framework;

    /// <summary>
    /// A build task for typescript that supports incremental rebuild.
    /// </summary>
    public class VsTscEx : VsTsc
    {
        bool cacheDirty = true;
        string commonPath;
        int emtCount;
        int emtMiss;
        Dictionary<string, DependencyRecord> records;

        /// <summary>
        /// Gets or sets the file name for the dependency cache. If unset, dependencies will not be cached.
        /// </summary>
        public string DependencyCache
        {
            get;
            set;
        }

        /// <summary>
        /// Considers the file at the specified path for recompilation, consulting (and updating) the local cache as
        /// necessary.
        /// </summary>
        /// <param name="fullPath">The full path of the file to consider.</param>
        /// <returns>true if the file has changed and should be recompiled, false if not.</returns>
        bool Consider(string fullPath)
        {
            try
            {
                string outputFile = ComputeOutputFile(fullPath);
                if (outputFile != null)
                {
                    if (!File.Exists(outputFile))
                    {
                        Log.LogMessage(
                            MessageImportance.Low,
                            "Recompile '{0}' because output file '{1}' does not exist.",
                            fullPath,
                            outputFile);
                        return true;
                    }

                    DateTime lastWrite = GetEffectiveModifiedTime(fullPath);
                    DateTime outputWrite = File.GetLastWriteTimeUtc(outputFile);
                    if (lastWrite > outputWrite)
                    {
                        Log.LogMessage(
                            MessageImportance.Low,
                            "Recompile '{0}' because output file '{1}' is out of date.",
                            fullPath,
                            outputFile);
                        return true;
                    }
                }
                else
                {
                    // No output file: who can say? User must have added it to their stuff for a reason. Kids: don't 
                    // mark your .d.ts files as TypeScriptCompile?
                    Log.LogMessage(MessageImportance.Low, "Recompile '{0}' because output file is null.", fullPath);
                    return true;
                }

                return false;
            }
            catch (IOException error)
            {
                Log.LogMessage(
                    MessageImportance.Low,
                    "Error accessing {0}, assuming recompilation. ({1})",
                    fullPath,
                    error.Message);

                return true;
            }
        }

        /// <summary>
        /// Computes the shared path of the files. I don't know why the TSC task does this, but we copy them.
        /// </summary>
        /// <returns>The common subdirectory shared by all of the input files.</returns>
        string ComputeCommonDirectoryPath()
        {
            string text = string.Empty;

            string[] sharedParts = null;
            int sharedLength = -1;
            for (int i = 0; i < this.FullPathsToFiles.Length; i++)
            {
                string fullPath = GetFullPath(this.FullPathsToFiles[i]);
                if (fullPath.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)) { continue; }

                string[] segments = fullPath.Split(Path.DirectorySeparatorChar);
                if (sharedLength == -1)
                {
                    sharedParts = segments;
                    sharedLength = segments.Length;
                    continue;
                }

                for (int seg = 0; seg < sharedLength; seg++)
                {
                    if (seg == segments.Length)
                    {
                        sharedLength = segments.Length;
                        break;
                    }

                    if (!String.Equals(sharedParts[seg], segments[seg], StringComparison.OrdinalIgnoreCase))
                    {
                        sharedLength = seg;
                        if (sharedLength == 0) { return null; } // No common!
                    }
                }
            }

            string result = sharedParts[0];
            for (int i = 1; i < sharedLength; i++)
            {
                result = Path.Combine(result, sharedParts[i]);
            }
            return result;
        }

        /// <summary>
        /// Determines the effective modified time for the given file, which is latest of either the file's 
        /// last-write-time, or the effective modified times of any of the file's dependencies.
        /// </summary>
        /// <param name="fullPath">The file to get the effective modified time for.</param>
        /// <returns>The effective modified time for the file.</returns>
        DateTime GetEffectiveModifiedTime(string fullPath)
        {
            this.emtCount++;

            DependencyRecord record = GetDependencyRecord(fullPath);
            if (record.EffectiveModifiedTime == null)
            {
                this.emtMiss++;

                // Set it here to stop cycles...
                DateTime modifiedTime = File.GetLastWriteTimeUtc(fullPath);
                record.EffectiveModifiedTime = modifiedTime;

                if (record.Update(fullPath, modifiedTime)) { this.cacheDirty = true; }
                for (int i = 0; i < record.Dependencies.Count; i++)
                {
                    DateTime dependentTime = GetEffectiveModifiedTime(record.Dependencies[i]);
                    if (dependentTime > modifiedTime) { modifiedTime = dependentTime; }
                }

                // And here because it's right...
                record.EffectiveModifiedTime = modifiedTime;
            }

            return record.EffectiveModifiedTime.Value;
        }

        /// <summary>
        /// Computes the full path of the file that would be created by compiling the specified input file.
        /// </summary>
        /// <param name="fullPath">The full path of the input file.</param>
        /// <returns>The full path of the output file, or null if no file would be created.</returns>
        string ComputeOutputFile(string fullPath)
        {
            if (fullPath.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)) { return null; }
            if (!String.IsNullOrEmpty(this.OutFile)) { return this.OutFile; }
            if (!String.IsNullOrEmpty(this.OutDir))
            {
                if (this.commonPath != null) { fullPath = fullPath.Replace(this.commonPath, ""); }
                fullPath = Path.GetFullPath(Path.Combine(OutDir, fullPath));
            }

            return Path.ChangeExtension(fullPath, "js");
        }

        public override bool Execute()
        {
            LoadDependencyCache();
            try
            {
                this.commonPath = ComputeCommonDirectoryPath();
                List<ITaskItem> inputs = GetInputsToRecompile(FullPathsToFiles);
                if (inputs.Count == 0)
                {
                    Log.LogMessage(MessageImportance.Normal, "Skipping typescript compile: all outputs up-to-date");
                    return true;
                }

                FullPathsToFiles = inputs.ToArray();
                return base.Execute();
            }
            finally
            {
                SaveDependencyCache();
            }
        }

        /// <summary>
        /// Determine the set of input files to recompile.
        /// </summary>
        /// <returns>The list of task items to recompile.</returns>
        List<ITaskItem> GetInputsToRecompile(IList<ITaskItem> inputs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Log.LogMessage(MessageImportance.Low, "Finding changed files");
            List<ITaskItem> outputs = new List<ITaskItem>();
            for (int i = 0; i < inputs.Count; i++)
            {
                string path = GetFullPath(inputs[i]);
                if (Consider(path)) { outputs.Add(inputs[i]); }
            }
            Log.LogMessage(MessageImportance.Low, "  Done in {0}ms", sw.ElapsedMilliseconds);
            Log.LogMessage(MessageImportance.Low, "  Computed {0} ({1}) EMTs", this.emtCount, this.emtMiss);

            return outputs;
        }

        /// <summary>
        /// Gets the dependency record of the specified file.
        /// </summary>
        /// <param name="fullPath">The full path of the file whose dependency record is sought.</param>
        /// <returns>The dependency record. It's dependencies may or may not be up-to-date; this allows us to save 
        /// extra filesystem calls.</returns>
        DependencyRecord GetDependencyRecord(string fullPath)
        {
            DependencyRecord record = null;
            if (!this.records.TryGetValue(fullPath, out record))
            {
                record = new DependencyRecord();
                this.records[fullPath] = record;
                this.cacheDirty = true;
            }

            return record;
        }

        /// <summary>
        /// Compute the full path for the given task item.
        /// </summary>
        /// <param name="item">The item for which we want fhe full path.</param>
        /// <returns>The requested path.</returns>
        static string GetFullPath(ITaskItem item)
        {
            return Path.GetFullPath(item.GetMetadata("FullPath"));
        }

        /// <summary>
        /// Attempts to load the dependency cache from disk.
        /// </summary>
        /// <remarks>We use a custom format here because time is of the essence: using JSON via JSON.NET took ~150ms 
        /// for my large project. This format takes 6ms.</remarks>
        void LoadDependencyCache()
        {
            if (!String.IsNullOrEmpty(DependencyCache) && File.Exists(DependencyCache))
            {
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    Log.LogMessage("Reading dependency cache from '{0}'", DependencyCache);

                    using (StreamReader reader = File.OpenText(DependencyCache))
                    {
                        int fileCount = Int32.Parse(reader.ReadLine());
                        var data = new Dictionary<string, DependencyRecord>(fileCount, StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < fileCount; i++)
                        {
                            string fname = reader.ReadLine();
                            DateTimeOffset lastScan = DateTimeOffset.Parse(reader.ReadLine());

                            int depCount = Int32.Parse(reader.ReadLine());
                            string[] deps = new string[depCount];
                            for (int id = 0; id < depCount; id++)
                            {
                                deps[id] = reader.ReadLine();
                            }

                            data.Add(fname, new DependencyRecord(lastScan, deps));
                        }

                        this.records = data;
                    }

                    this.cacheDirty = false;
                    Log.LogMessage(MessageImportance.Low, "  Loaded in {0}ms", sw.ElapsedMilliseconds);
                }
                catch (Exception e)
                {
                    Log.LogWarning(
                        "Could not load dependency cache from '{0}' (error: {1})",
                        DependencyCache,
                        e.Message);
                }
            }

            if (this.records == null)
            {
                this.records = new Dictionary<string, DependencyRecord>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Attempts to save the dependency cache to disk.
        /// </summary>
        /// <remarks>We use a custom format here because load time is of the essence; this save time doesn't matter
        /// much. (We assume the dependency cache is rarely out of date, and if it is, you'll be running the compiler
        /// anyway.)</remarks>
        void SaveDependencyCache()
        {
            if (!String.IsNullOrEmpty(DependencyCache) && this.cacheDirty)
            {
                try
                {
                    Log.LogMessage("Saving dependency cache to '{0}'", DependencyCache);
                    using (StreamWriter writer = File.CreateText(DependencyCache))
                    {
                        writer.WriteLine(this.records.Count);
                        foreach (KeyValuePair<string, DependencyRecord> kvp in this.records)
                        {
                            writer.WriteLine(kvp.Key);
                            writer.WriteLine(kvp.Value.LastScanned.ToString("o"));
                            writer.WriteLine(kvp.Value.Dependencies.Count);
                            for (int i = 0; i < kvp.Value.Dependencies.Count; i++)
                            {
                                writer.WriteLine(kvp.Value.Dependencies[i]);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.LogWarning(
                        "Could not save dependency cache to '{0}' (error: {1})",
                        DependencyCache,
                        e.Message);
                }
            }
        }

        /// <summary>
        /// A class to keep track of the dependency graph of our files.
        /// </summary>
        class DependencyRecord
        {
            static readonly Regex referenceTag = new Regex(
                @"^\s*///\s*<\s*reference\s*path\s*=\s*[""']([^""']+)[""']",
                RegexOptions.Multiline | RegexOptions.Compiled);

            readonly List<string> dependencies;

            public DependencyRecord()
            {
                LastScanned = DateTime.MinValue;
                this.dependencies = new List<string>();
            }

            public DependencyRecord(DateTimeOffset lastScanned, string[] deps)
            {
                LastScanned = lastScanned;
                this.dependencies = new List<string>(deps);
            }

            public IList<string> Dependencies { get { return this.dependencies; } }
            public DateTimeOffset LastScanned { get; set; }
            public DateTime? EffectiveModifiedTime { get; set; }

            /// <summary>
            /// Scans the file specified by <paramref name="fullPath"/> and fills in the list of dependencies.
            /// </summary>
            /// <param name="fullPath">The path of the file to scan.</param>
            /// <param name="lastWriteTime">The last write time of the file, or null if unknown.</param>
            /// <returns>true if the dependencies were updated, otherwise false.</returns>
            /// <remarks>This could be both faster and more accurate if it didn't use regular expressions. Alas, it 
            /// would also not be *done*.</remarks>
            public bool Update(string fullPath, DateTimeOffset lastWriteTime)
            {
                if (LastScanned == lastWriteTime) { return false; }

                string content = File.ReadAllText(fullPath);
                string dir = Path.GetDirectoryName(fullPath);

                this.dependencies.Clear();
                foreach (Match match in referenceTag.Matches(content))
                {
                    this.dependencies.Add(Path.GetFullPath(Path.Combine(dir, match.Groups[1].Value)));
                }

                LastScanned = lastWriteTime;
                return true;
            }
        }

    }
}
