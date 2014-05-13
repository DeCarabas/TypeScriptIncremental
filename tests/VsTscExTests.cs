namespace TypeScript.Tasks.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Doty.Spec;
    using Microsoft.Build.Evaluation;
    using Microsoft.Build.Execution;
    using Microsoft.Build.Framework;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    [DeploymentItem(@"TypeScript.Incremental.targets")]
    [DeploymentItem(@"testproject\", @"testproject\")]
    public class VsTscExTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void DescribeVsTscEx()
        {
            string projectDirectory = @"testproject";
            string baseOutput = Path.Combine(projectDirectory, "base.js");
            string topOutput = Path.Combine(projectDirectory, "top.js");
            string thirdOutput = Path.Combine(projectDirectory, "third.js");

            Project project = null;
            ProjectCollection projectCollection = null;
            Logger logger = null;
            BuildResult result = null;
            DateTime lastWriteTimeTop = DateTime.MinValue;
            DateTime lastWriteTimeBase = DateTime.MinValue;
            DateTime lastWriteTimeThird = DateTime.MinValue;
            ITaskItem[] generatedJavascript = null;

            // Test it all.
            XSpec
                .Given("A project with some typescript", () =>
                {
                    projectCollection = new ProjectCollection();
                    project = projectCollection.LoadProject(Path.Combine(projectDirectory, "testproject.jsproj"));

                    // A special hook for tracking the output parameter of the task.
                    generatedJavascript = null;
                    VsTscEx.AfterBuildHook = (task) => { generatedJavascript = task.GeneratedJavascript; };                    

                    logger = new Logger();
                })
                .When("I am just starting out", () => { })
                    .It("doesn't have js files", () => Assert.AreEqual(
                        0, Directory.GetFiles(projectDirectory, "*.js", SearchOption.AllDirectories).Length))

                .When("I build the project", () => result = Build(projectCollection, project, logger))
                    .It("succeeds", () => Assert.AreEqual(BuildResultCode.Success, result.OverallResult))
                    .It("still didn't generate any files", () => Assert.AreEqual(
                        0, Directory.GetFiles(projectDirectory, "*.js", SearchOption.AllDirectories).Length))

                .When("I add the TypeScriptCompile items to the project", () =>
                    {
                        project.AddItem("TypeScriptCompile", "top.ts");
                        project.AddItem("TypeScriptCompile", "base.ts");
                        project.AddItem("TypeScriptCompile", "third.ts");
                    })
                .When("I build the project", () => result = Build(projectCollection, project, logger))
                    .It("builds successfully", () => Assert.AreEqual(BuildResultCode.Success, result.OverallResult))
                    .It("created all the JS files", () =>
                    {
                        Assert.AreEqual(
                            3, Directory.GetFiles(projectDirectory, "*.js", SearchOption.AllDirectories).Length);
                    })

                .When("I gather the write times, and do a no-op build", () =>
                    {
                        generatedJavascript = null;
                        lastWriteTimeTop = File.GetLastWriteTime(topOutput);
                        lastWriteTimeBase = File.GetLastWriteTime(baseOutput);
                        lastWriteTimeThird = File.GetLastWriteTime(thirdOutput);

                        result = Build(projectCollection, project, logger);
                    })
                    .It("builds successfully", () => Assert.AreEqual(BuildResultCode.Success, result.OverallResult))
                    .It("didn't touch the base output",
                        () => Assert.AreEqual(lastWriteTimeBase, File.GetLastWriteTime(baseOutput)))
                    .It("didn't touch the third output",
                        () => Assert.AreEqual(lastWriteTimeThird, File.GetLastWriteTime(thirdOutput)))
                    .It("didn't touch the top output",
                        () => Assert.AreEqual(lastWriteTimeTop, File.GetLastWriteTime(topOutput)))
                    .It("returns all of the output javascript files",
                        () => Assert.AreEqual(3, generatedJavascript.Length))

                .When("I set the TypeScriptOutFile property", 
                    () => projectCollection.SetGlobalProperty("TypeScriptOutFile", "zomg.js"))
                .When("I rebuild the project", () => result = Build(projectCollection, project, logger))
                    .It("fails the build", () => Assert.AreEqual(BuildResultCode.Failure, result.OverallResult))

                .When("I reset the TypeScriptOutFile property", 
                    () => projectCollection.RemoveGlobalProperty("TypeScriptOutFile"))
                .When("I touch the third file", () =>
                    {
                        generatedJavascript = null;
                        lastWriteTimeTop = File.GetLastWriteTime(topOutput);
                        lastWriteTimeBase = File.GetLastWriteTime(baseOutput);
                        lastWriteTimeThird = File.GetLastWriteTime(thirdOutput);

                        File.SetLastWriteTime(Path.Combine(projectDirectory, "third.ts"), DateTime.UtcNow);
                    })
                .When("I build the project", () => result = Build(projectCollection, project, logger))
                    .It("builds successfully", () => Assert.AreEqual(BuildResultCode.Success, result.OverallResult))
                    .It("didn't touch the base output", 
                        () => Assert.AreEqual(lastWriteTimeBase, File.GetLastWriteTime(baseOutput)))
                    .It("did touch the third output", 
                        () => Assert.AreNotEqual(lastWriteTimeThird, File.GetLastWriteTime(thirdOutput)))
                    .It("didn't touch the top output", 
                        () => Assert.AreEqual(lastWriteTimeTop, File.GetLastWriteTime(topOutput)))
                    .It("returns all of the output javascript files", 
                        () => Assert.AreEqual(3, generatedJavascript.Length))

                .When("I touch the base file", () =>
                    {
                        generatedJavascript = null;
                        lastWriteTimeTop = File.GetLastWriteTime(topOutput);
                        lastWriteTimeBase = File.GetLastWriteTime(baseOutput);
                        lastWriteTimeThird = File.GetLastWriteTime(thirdOutput);

                        File.SetLastWriteTime(Path.Combine(projectDirectory, "base.ts"), DateTime.UtcNow);
                    })
                .When("I rebuild the project", () => result = Build(projectCollection, project, logger))
                    .It("builds successfully", () => Assert.AreEqual(BuildResultCode.Success, result.OverallResult))
                    .It("rebuilds the base file", 
                        () => Assert.AreNotEqual(lastWriteTimeBase, File.GetLastWriteTime(baseOutput)))
                    .It("rebuilds the top file", 
                        () => Assert.AreNotEqual(lastWriteTimeTop, File.GetLastWriteTime(topOutput)))
                    .It("does not rebuild the third file", 
                        () => Assert.AreEqual(lastWriteTimeThird, File.GetLastWriteTime(thirdOutput)))
                    .It("returns all of the output javascript files",
                        () => Assert.AreEqual(3, generatedJavascript.Length))

                .When("I delete the base output file", () =>
                    {
                        generatedJavascript = null;
                        lastWriteTimeTop = File.GetLastWriteTime(Path.Combine(projectDirectory, "top.js"));
                        lastWriteTimeBase = File.GetLastWriteTime(Path.Combine(projectDirectory, "base.js"));
                        lastWriteTimeThird = File.GetLastWriteTime(Path.Combine(projectDirectory, "third.js"));

                        File.Delete(baseOutput);
                    })
                .When("I rebuild the project", () => result = Build(projectCollection, project, logger))
                    .It("rebuilds the base file", 
                        () => Assert.AreNotEqual(lastWriteTimeBase, File.GetLastWriteTime(baseOutput)))
                    .It("does not rebuild the top file",
                        () => Assert.AreEqual(lastWriteTimeTop, File.GetLastWriteTime(topOutput)))
                    .It("does not rebuild the third file",
                        () => Assert.AreEqual(lastWriteTimeThird, File.GetLastWriteTime(thirdOutput)))
                    .It("returns all of the output javascript files",
                        () => Assert.AreEqual(3, generatedJavascript.Length))
                
                .Go();
        }

        static BuildResult Build(ProjectCollection projectCollection, Project project, Logger logger)
        {
            return new BuildManager().Build(
                new BuildParameters(projectCollection) { Loggers = new[] { logger } },
                new BuildRequestData(project.CreateProjectInstance(), new[] { "Build" }));
        }

        class Logger : ILogger
        {
            readonly List<BuildEventArgs> buildEvents = new List<BuildEventArgs>();

            public IList<BuildEventArgs> BuildEvents
            {
                get { return this.buildEvents; }
            }

            public string Parameters
            {
                get { return String.Empty; }
                set { }
            }

            public LoggerVerbosity Verbosity
            {
                get { return LoggerVerbosity.Diagnostic; }
                set { }
            }

            static string FormatError(BuildErrorEventArgs error)
            {
                return string.Format("{0}({1}): {2}", error.File, error.LineNumber, error.Message);
            }

            public void Initialize(IEventSource eventSource)
            {
                eventSource.AnyEventRaised += OnEvent;
            }

            void OnEvent(object sender, BuildEventArgs args)
            {
                var error = args as BuildErrorEventArgs;
                if (error != null)
                {
                    Console.WriteLine("ERROR: {0}", FormatError(error));
                }

                this.buildEvents.Add(args);
            }

            public void Shutdown()
            {

            }
        }

    }
}
