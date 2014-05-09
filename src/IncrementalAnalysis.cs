namespace TypeScript.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;

    /// <summary>
    /// Routines to support incremental analysis-- figuring out what needs to be recompiled.
    /// </summary>
    public static class IncrementalAnalysis
    {
        /// <summary>
        /// Considers the file at the specified path for recompilation, consulting (and updating) the provided cache as
        /// necessary.
        /// </summary>
        /// <param name="fullPath">The full path of the file to consider.</param>
        /// <param name="cache">The dependency cache to use while evaluating changes.</param>
        /// <param name="log">A log to write progress and results to.</param>
        /// <param name="computeOutputPath">A function that computes the output path for a given input path.</param>
        /// <returns>true if the file has changed and should be recompiled, false if not.</returns>
        public static bool Consider(
            string fullPath, DependencyCache cache, TaskLoggingHelper log, Func<string, string> computeOutputPath)
        {
            try
            {
                string outputFile = computeOutputPath(fullPath);
                if (outputFile != null)
                {
                    if (!File.Exists(outputFile))
                    {
                        log.LogMessage(
                            MessageImportance.Low,
                            "Recompile '{0}' because output file '{1}' does not exist.",
                            fullPath,
                            outputFile);
                        return true;
                    }

                    DateTime lastWrite = cache.GetEffectiveModifiedTime(fullPath);
                    DateTime outputWrite = File.GetLastWriteTimeUtc(outputFile);
                    if (lastWrite > outputWrite)
                    {
                        log.LogMessage(
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
                    log.LogMessage(MessageImportance.Low, "Recompile '{0}' because output file is null.", fullPath);
                    return true;
                }

                return false;
            }
            catch (IOException error)
            {
                log.LogMessage(
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
        /// <param name="items">The items to compute the common path of.</param>
        /// <returns>The common subdirectory shared by all of the input files.</returns>
        public static string ComputeCommonDirectoryPath(IList<ITaskItem> items)
        {
            string[] paths = new string[items.Count];
            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = GetFullPath(items[i]);
            }

            return ComputeCommonDirectoryPath(paths);
        }

        /// <summary>
        /// Computes the shared path of the files. I don't know why the TSC task does this, but we copy them.
        /// </summary>
        /// <param name="paths">The paths to compute the common path of.</param>
        /// <returns>The common subdirectory shared by all of the input files.</returns>
        public static string ComputeCommonDirectoryPath(IList<string> paths)
        {
            string[] sharedParts = null;
            int sharedLength = -1;
            for (int i = 0; i < paths.Count; i++)
            {
                string fullPath = paths[i];
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
        /// <param name="commonPath">The common path prefix shared by all inputs; this is removed from the final path.
        /// </param>
        /// <param name="outputDirectory">The target output directory for the file.</param>
        /// <returns>The full path of the output file, or null if no file would be created.</returns>
        public static string ComputeOutputFile(string fullPath, string commonPath, string outputDirectory)
        {
            if (fullPath.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)) { return null; }

            if (commonPath != null) { fullPath = fullPath.Replace(commonPath, ""); }
            fullPath = Path.GetFullPath(Path.Combine(outputDirectory, fullPath));

            return Path.ChangeExtension(fullPath, "js");
        }

        /// <summary>
        /// Determine the set of input files to recompile.
        /// </summary>
        /// <returns>The list of task items to recompile.</returns>
        public static List<ITaskItem> GetInputsToRecompile(
            DependencyCache cache,
            IList<ITaskItem> inputs,
            TaskLoggingHelper log,
            Func<string, string> computeOutputPath)
        {
            Stopwatch sw = Stopwatch.StartNew();
            log.LogMessage(MessageImportance.Low, "Finding changed files");
            List<ITaskItem> outputs = new List<ITaskItem>();
            for (int i = 0; i < inputs.Count; i++)
            {
                string path = GetFullPath(inputs[i]);
                if (Consider(path, cache, log, computeOutputPath)) { outputs.Add(inputs[i]); }
            }
            log.LogMessage(MessageImportance.Low, "  Done in {0}ms", sw.ElapsedMilliseconds);

            return outputs;
        }

        /// <summary>
        /// Compute the full path for the given task item.
        /// </summary>
        /// <param name="item">The item for which we want fhe full path.</param>
        /// <returns>The requested path.</returns>
        public static string GetFullPath(ITaskItem item)
        {
            return Path.GetFullPath(item.GetMetadata("FullPath"));
        }
    }
}
