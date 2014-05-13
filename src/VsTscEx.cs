/*
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
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;

    /// <summary>
    /// A build task for typescript that supports incremental rebuild.
    /// </summary>
    public class VsTscEx : Task
    {
        /// <summary>
        /// Gets or sets a callback that is called at the end of execution for all builds in the appdomain-- this is 
        /// used by tests.
        /// </summary>
        public static Action<VsTscEx> AfterBuildHook { get; set; }

        /// <summary>
        /// Gets or sets the configurations for the TSC compiler.
        /// </summary>
        public string Configurations { get; set; }

        /// <summary>
        /// Gets or sets the file name for the dependency cache. If unset, dependencies will not be cached.
        /// </summary>
        public string DependencyCache
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the input files for the compilation (regardless of dependencies).
        /// </summary>
        public ITaskItem[] FullPathsToFiles { get; set; }

        /// <summary>
        /// Gets or sets the javascript files that result from the compilation (regardless of if the input file was 
        /// computed or not).
        /// </summary>
        [Output]
        public ITaskItem[] GeneratedJavascript { get; set; }

        /// <summary>
        /// Gets or sets the output directory for the generated javascript.
        /// </summary>
        public string OutDir { get; set; }

        /// <summary>
        /// Gets or sets the output file for the generated javascript.
        /// </summary>
        public string OutFile { get; set; }

        /// <summary>
        /// Gets or sets the project directory.
        /// </summary>
        public string ProjectDir { get; set; }

        /// <summary>
        /// Gets or sets the name of the typescript compiler EXE.
        /// </summary>
        public string ToolExe { get; set; }

        /// <summary>
        /// Gets or sets the full path to the typescript compiler EXE.
        /// </summary>
        public string ToolPath { get; set; }

        /// <summary>
        /// Gets or sets a switch that indicates if the task will yield the node during tool execution.
        /// </summary>
        public bool YieldDuringToolExecution { get; set; }

        bool Compile(ITaskItem[] itemsToCompile)
        {
            var innerTask = new VsTsc
            {
                BuildEngine = BuildEngine,
                Configurations = Configurations,
                FullPathsToFiles = itemsToCompile,
                HostObject = HostObject,
                OutDir = OutDir,
                OutFile = OutFile,
                ProjectDir = ProjectDir,
                ToolExe = ToolExe,
                ToolPath = ToolPath,
                YieldDuringToolExecution = YieldDuringToolExecution,
            };

            return innerTask.Execute();
        }

        /// <summary>
        /// Executes the task.
        /// </summary>
        /// <returns>true if the task was successful, otherwise false</returns>
        public override bool Execute()
        {
            if (!String.IsNullOrEmpty(OutFile))
            {
                Log.LogError("The OutFile parameter on the VsTscEx task is not supported right now, sorry.");
                return false;
            }

            ITaskItem[] generatedJavascript;
            bool result = IncrementalAnalysis.CompileIncremental(
                this.FullPathsToFiles,
                this.OutDir,
                this.DependencyCache,
                this.Log,
                this.Compile,
                out generatedJavascript);
            GeneratedJavascript = generatedJavascript;

            if (AfterBuildHook != null) { AfterBuildHook(this); }

            return result;
        }
    }
}
