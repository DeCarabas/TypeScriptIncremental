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
        DependencyCache cache;
        string commonPath;

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

                    DateTime lastWrite = this.cache.GetEffectiveModifiedTime(fullPath);
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
        /// Computes the full path of the file that would be created by compiling the specified input file.
        /// </summary>
        /// <param name="fullPath">The full path of the input file.</param>
        /// <returns>The full path of the output file, or null if no file would be created.</returns>
        string ComputeOutputFile(string fullPath)
        {
            if (fullPath.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)) { return null; }

            if (this.commonPath != null) { fullPath = fullPath.Replace(this.commonPath, ""); }
            fullPath = Path.GetFullPath(Path.Combine(OutDir, fullPath));

            return Path.ChangeExtension(fullPath, "js");
        }

        /// <summary>
        /// Executes the task.
        /// </summary>
        /// <returns>true if the task was successful, otherwise false</returns>
        public override bool Execute()
        {
            if (!String.IsNullOrEmpty(OutFile))
            {
                Log.LogError(
                    "Incremental rebuild doesn't work when OutFile is set; use the core task for the moment.");
                return false;
            }

            if (String.IsNullOrEmpty(OutDir))
            {
                Log.LogError("You must specify the OutDir parameter.");
                return false;
            }

            return ExecuteIncremental();
        }

        /// <summary>
        /// Runs an incremental rebuild of TypeScript files.
        /// </summary>
        /// <returns>true if execution was successful, otherwise false.</returns>
        bool ExecuteIncremental()
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

            return outputs;
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
                        this.cache = new DependencyCache(reader);
                    }

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

            if (this.cache == null)
            {
                this.cache = new DependencyCache();
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
            if (!String.IsNullOrEmpty(DependencyCache) && this.cache.IsModified)
            {
                try
                {
                    Log.LogMessage("Saving dependency cache to '{0}'", DependencyCache);
                    using (StreamWriter writer = File.CreateText(DependencyCache))
                    {
                        this.cache.Write(writer);
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
    }
}
