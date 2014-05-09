namespace TypeScript.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;

    /// <summary>
    /// A build task for typescript that supports incremental rebuild.
    /// </summary>
    public class VsTscEx : VsTsc
    {
        /// <summary>
        /// Gets or sets the file name for the dependency cache. If unset, dependencies will not be cached.
        /// </summary>
        public string DependencyCache
        {
            get;
            set;
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
            DependencyCache cache = LoadDependencyCache(DependencyCache, Log);
            try
            {
                string commonPath = IncrementalAnalysis.ComputeCommonDirectoryPath(FullPathsToFiles);
                Func<string, string> computeOutputFile = 
                    (file) => IncrementalAnalysis.ComputeOutputFile(file, commonPath, OutDir);

                List<ITaskItem> inputs = IncrementalAnalysis.GetInputsToRecompile(
                    cache, FullPathsToFiles, Log, computeOutputFile);
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
                SaveDependencyCache(cache, DependencyCache, Log);
            }
        }

        /// <summary>
        /// Attempts to load the dependency cache from disk.
        /// </summary>
        /// <remarks>We use a custom format here because time is of the essence: using JSON via JSON.NET took ~150ms 
        /// for my large project. This format takes 6ms.</remarks>
        static DependencyCache LoadDependencyCache(string path, TaskLoggingHelper log)
        {
            DependencyCache cache = null;
            if (!String.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    log.LogMessage("Reading dependency cache from '{0}'", path);

                    using (StreamReader reader = File.OpenText(path))
                    {
                        cache = new DependencyCache(reader);
                    }

                    log.LogMessage(MessageImportance.Low, "  Loaded in {0}ms", sw.ElapsedMilliseconds);
                }
                catch (Exception e)
                {
                    log.LogWarning(
                        "Could not load dependency cache from '{0}' (error: {1})",
                        path,
                        e.Message);
                }
            }

            return cache ?? new DependencyCache();
        }

        /// <summary>
        /// Attempts to save the dependency cache to disk.
        /// </summary>
        /// <remarks>We use a custom format here because load time is of the essence; this save time doesn't matter
        /// much. (We assume the dependency cache is rarely out of date, and if it is, you'll be running the compiler
        /// anyway.)</remarks>
        static void SaveDependencyCache(DependencyCache cache, string path, TaskLoggingHelper log)
        {
            if (!String.IsNullOrEmpty(path) && cache.IsModified)
            {
                try
                {
                    log.LogMessage("Saving dependency cache to '{0}'", path);
                    using (StreamWriter writer = File.CreateText(path))
                    {
                        cache.Write(writer);
                    }
                }
                catch (Exception e)
                {
                    log.LogWarning(
                        "Could not save dependency cache to '{0}' (error: {1})",
                        path,
                        e.Message);
                }
            }
        }
    }
}
