﻿/*
Copyright (C) 2014 John Doty

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
namespace TypeScript.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;

    /// <summary>
    /// Routines to support incremental analysis-- figuring out what needs to be recompiled.
    /// </summary>
    public static class IncrementalAnalysis
    {
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

                string[] segments = Path.GetDirectoryName(fullPath).Split(Path.DirectorySeparatorChar);
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
            if (result.EndsWith(":")) { result += @"\"; }
            for (int i = 1; i < sharedLength; i++)
            {
                result = Path.Combine(result, sharedParts[i]);
            }
            if (!result.EndsWith(@"\")) { result += @"\"; }
            return result;
        }

        /// <summary>
        /// Computes the set of generated javascript files based on the specified output mapping.
        /// </summary>
        /// <param name="outputMapping">The mapping of input typescript to output javascript, generated by a call to 
        /// <see cref="ComputeOutputMapping"/>.</param>
        /// <returns>An array of ITaskItems, each corresponding to an input typescript file.</returns>
        public static ITaskItem[] ComputeGeneratedItems(IDictionary<string, string> outputMapping)
        {
            return outputMapping.Values
                .Where(f => f != null)
                .Select(f => new TaskItem(f))
                .ToArray<ITaskItem>();
        }

        /// <summary>
        /// Computes the mapping from input typescript files to output javascript files.
        /// </summary>
        /// <param name="inputs">The set of input typescript files.</param>
        /// <param name="outputPath">The output directory for compilation.</param>
        /// <returns>The mapping from input file name to output file name.</returns>
        public static IDictionary<string, string> ComputeOutputMapping(ITaskItem[] inputs, string outputPath)
        {
            string commonPath = outputPath != null
                ? ComputeCommonDirectoryPath(inputs)
                : null;

            var outputMapping = new Dictionary<string, string>(inputs.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < inputs.Length; i++)
            {
                string inputFile = GetFullPath(inputs[i]);
                outputMapping[inputFile] = ComputeOutputFile(inputFile, commonPath, outputPath);
            }

            return outputMapping;
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
            if (outputDirectory != null) { fullPath = Path.Combine(outputDirectory, fullPath); }            

            return Path.ChangeExtension(fullPath, "js");
        }

        /// <summary>
        /// Executes the typescript compiler, with the specified parameters.
        /// </summary>
        /// <param name="inputItems">The input typescript files to compile.</param>
        /// <param name="outputDirectory">The base directory in which to write the compiled files.</param>
        /// <param name="cacheFile">The full path to the cache file; if null then dependencies will not be cached.
        /// </param>
        /// <param name="log">The log to write progress, errors, &amp;c. to.</param>
        /// <param name="compile">A delegate to call to compile the files that actually need compiling. Should return
        /// true on success, and false on failure, like a task would.</param>
        /// <param name="generatedJavascript">The set of javascript files generated by compilation, whether or not 
        /// the input files were actually compiled.</param>
        /// <returns>true if the execution was successful, false otherwise.</returns>
        public static bool CompileIncremental(
            ITaskItem[] inputItems,
            string outputDirectory,
            string cacheFile,
            TaskLoggingHelper log,
            Func<ITaskItem[], bool> compile,
            out ITaskItem[] generatedJavascript)
        {
            DependencyCache cache = LoadDependencyCache(cacheFile, log);
            try
            {
                IDictionary<string, string> outputMapping;
                outputMapping = ComputeOutputMapping(inputItems, outputDirectory);
                generatedJavascript = ComputeGeneratedItems(outputMapping);

                Func<string, string> computeOutputFile = (f) => outputMapping[f];
                List<ITaskItem> recompileItems = GetInputsToRecompile(cache, inputItems, log, computeOutputFile);
                if (recompileItems.Count == 0)
                {
                    log.LogMessage(MessageImportance.Normal, "Skipping typescript compile: all outputs up-to-date");
                    return true;
                }

                return compile(recompileItems.ToArray());
            }
            finally
            {
                SaveDependencyCache(cache, cacheFile, log);
            }
        }

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

        /// <summary>
        /// Attempts to load the dependency cache from disk.
        /// </summary>
        /// <remarks>We use a custom format here because time is of the essence: using JSON via JSON.NET took ~150ms 
        /// for my large project. This format takes 6ms.</remarks>
        public static DependencyCache LoadDependencyCache(string path, TaskLoggingHelper log)
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
        public static void SaveDependencyCache(DependencyCache cache, string path, TaskLoggingHelper log)
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
