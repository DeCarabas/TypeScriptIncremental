using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Doty.Spec;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace TypeScript.Tasks.Tests
{
    [TestClass]
    public class IncrementalAnalysisTests
    {
        string baseDir;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void TestInitialize()
        {
            this.baseDir = Path.Combine(TestContext.DeploymentDirectory, Guid.NewGuid().ToString());
            Directory.CreateDirectory(this.baseDir);
        }

        [TestMethod]
        public void DescribeComputeCommonDirectoryPath()
        {
            var cases = new[] 
            {
                new { paths = new[] { @"c:\a\b", @"c:\a\c" },            result = @"c:\a" },
                new { paths = new[] { @"c:\a\b", @"c:\a\c", @"c:\a\d" }, result = @"c:\a" },
                new { paths = new[] { @"c:\a\b\c", @"c:\a\b\d" },        result = @"c:\a\b" },
                new { paths = new[] { @"c:\a\b", @"c:\c\d" },            result = @"c:\" },
                new { paths = new[] { @"c:\a\b", @"d:\a\b" },            result = (string)null },
                new { paths = new[] { @"c:\a\b", @"d:\e\f" },            result = (string)null },
                new { paths = new[] { @"c:\a\b", @"d:\e\f.d.ts" },       result = @"c:\a\b" },
            };

            for(int i =0; i < cases.Length; i++)
            {
                Assert.AreEqual(
                    cases[i].result, 
                    IncrementalAnalysis.ComputeCommonDirectoryPath(cases[i].paths));

                ITaskItem[] items = cases[i].paths.Select(p => CreateTaskItem(p)).ToArray();
                Assert.AreEqual(
                    cases[i].result,
                    IncrementalAnalysis.ComputeCommonDirectoryPath(items));
            }
        }

        [TestMethod]
        public void DescribeConsiderStandaloneFile()
        {
            DateTime? none = null;
            DateTime? current = DateTime.UtcNow;
            DateTime? older = current.Value.AddHours(-1);
            DateTime? newer = current.Value.AddHours(1);

            Func<DateTime?, string> desc = (dt) =>
                {
                    if (dt == none) { return "none"; }
                    if (dt == current) { return "current"; }
                    if (dt == older) { return "older"; }
                    if (dt == newer) { return "newer"; }
                    return "??";
                };

            var cases = new[] {
                new { input = current, output = none,    needRecompile = true },
                new { input = current, output = current, needRecompile = false },
                new { input = older,   output = current, needRecompile = false },
                new { input = newer,   output = current, needRecompile = true },
            };

            for (int i = 0; i < cases.Length; i++)
            {
                // Description
                //
                string caseDescription = String.Format(
                    "input: {0}, output: {1}, needRecompile: {2}",
                    desc(cases[i].input),
                    desc(cases[i].output),
                    cases[i].needRecompile);
                Console.WriteLine("Running case {0}: {1}", i, caseDescription);

                // Setup
                //
                string fileName = Path.GetTempFileName();
                File.SetLastWriteTimeUtc(fileName, cases[i].input.Value);

                string outputFileName = fileName + ".js";
                Func<string, string> getOutput = (fn) => outputFileName;
                if (cases[i].output != none)
                {
                    File.WriteAllText(outputFileName, "");
                    File.SetLastWriteTimeUtc(outputFileName, cases[i].output.Value);
                }

                // Execution
                //
                TaskLoggingHelper log = CreateFakeLog();
                DependencyCache cache = new DependencyCache();
                bool result = IncrementalAnalysis.Consider(fileName, cache, log, getOutput);

                // Analysis
                //
                Assert.AreEqual(cases[i].needRecompile, result, "Mismatch on test case {0} ({1})", i, caseDescription);
            }
        }

        [TestMethod]
        public void DescribeConsiderWithDependency()
        {
            DateTime? none = null;
            DateTime? current = DateTime.UtcNow;
            DateTime? older = current.Value.AddHours(-1);
            DateTime? newer = current.Value.AddHours(1);

            var cases = new[] {
                new { topIn = current, topOut = none,    baseIn = current, baseOut = current, doTop = true,  doBase = false },
                new { topIn = current, topOut = older,   baseIn = current, baseOut = current, doTop = true,  doBase = false },
                new { topIn = current, topOut = newer,   baseIn = current, baseOut = none,    doTop = false, doBase = true },
                new { topIn = current, topOut = current, baseIn = newer,   baseOut = current, doTop = true,  doBase = true },
                new { topIn = current, topOut = current, baseIn = older,   baseOut = older,   doTop = false, doBase = false },
                new { topIn = current, topOut = newer,   baseIn = newer,   baseOut = current, doTop = false, doBase = true },
            };

            Func<DateTime?, string> desc = (dt) =>
            {
                if (dt == none) { return "none"; }
                if (dt == current) { return "current"; }
                if (dt == older) { return "older"; }
                if (dt == newer) { return "newer"; }
                return "??";
            };

            for (int i = 0; i < cases.Length; i++)
            {
                // Description
                //
                string caseDescription = String.Format(
                    "topIn: {0}, topOut: {1}, baseIn: {2}, baseOut: {3}, doTop: {4}, doBase: {5}",
                    desc(cases[i].topIn),
                    desc(cases[i].topOut),
                    desc(cases[i].baseIn),
                    desc(cases[i].baseOut),
                    cases[i].doTop,
                    cases[i].doBase);

                Console.WriteLine("Running case {0}: {1}", i, caseDescription);

                // Setup
                //
                string baseName;
                string baseOut;
                CreateTestFile(cases[i].baseIn, cases[i].baseOut, null, out baseName, out baseOut);
                ITaskItem baseItem = CreateTaskItem(baseName);

                string topName;
                string topOut;
                CreateTestFile(cases[i].topIn, cases[i].topOut, new string[] { baseName }, out topName, out topOut);
                ITaskItem topItem = CreateTaskItem(topName);

                Func<string, string> getOutput = (f) =>
                    {
                        if (f == baseName) { return baseOut; }
                        if (f == topName) { return topOut; }
                        Assert.Fail("Was asked for the output name for an unrecognized input: {0}", f);
                        throw new ArgumentOutOfRangeException("f");
                    };

                // Execution
                //
                TaskLoggingHelper log = CreateFakeLog();
                DependencyCache cache = new DependencyCache();
                bool resultTop = IncrementalAnalysis.Consider(topName, cache, log, getOutput);
                bool resultBase = IncrementalAnalysis.Consider(baseName, cache, log, getOutput);
                IList<ITaskItem> recompiles = IncrementalAnalysis.GetInputsToRecompile(
                    cache, new[] { baseItem, topItem }, log, getOutput);

                // Analysis
                //
                Assert.AreEqual(cases[i].doTop, resultTop, "Mismatch on doTop in case {0} ({1})", i, caseDescription);
                Assert.AreEqual(cases[i].doBase, resultBase, "Mismatch on doBase in case {0} ({1})", i, caseDescription);
                AssertMaybeContains(recompiles, topItem, cases[i].doTop, i, caseDescription);
                AssertMaybeContains(recompiles, baseItem, cases[i].doBase, i, caseDescription);
            }
        }

        [TestMethod]
        public void DescribeConsiderIOException()
        {
            bool result = IncrementalAnalysis.Consider(
                "foo", 
                new DependencyCache(), 
                CreateFakeLog(), 
                f => { throw new IOException("WUT"); });

            Assert.IsTrue(result, "We should be recompiling files where we get an IO error.");
        }

        [TestMethod]
        public void DescribeConsiderNoOutput()
        {
            bool result = IncrementalAnalysis.Consider("foo", new DependencyCache(), CreateFakeLog(), f => null);
            Assert.IsTrue(result, "We should be recompiling files with no output (by fiat, at this time).");
        }

        static void AssertMaybeContains(
            IList<ITaskItem> items, ITaskItem item, bool shouldContain, int caseIndex, string caseDescription)
        {
            Assert.AreEqual(
                shouldContain,
                items.Contains(item),
                "Items should{0} contain top in case {1} ({2})",
                shouldContain
                    ? ""
                    : " not",
                caseIndex,
                caseDescription);
        }

        TaskLoggingHelper CreateFakeLog()
        {
            return new TaskLoggingHelper(new FakeBuildEngine(), "Fake");
        }

        ITaskItem CreateTaskItem(string path)
        {
            return new TaskItem(path);
        }

        void CreateTestFile(
            DateTime? inputTime,
            DateTime? outputTime,
            string[] dependencies,
            out string inFile,
            out string outFile)
        {
            inFile = Path.GetTempFileName();
            using (var writer = File.CreateText(inFile))
            {
                if (dependencies != null)
                {
                    for (int i = 0; i < dependencies.Length; i++)
                    {
                        writer.WriteLine(@"/// <reference path=""{0}"" />", dependencies[i]);
                    }
                }

                writer.WriteLine();
                writer.WriteLine("var x = 10;");
            }
            File.SetLastWriteTimeUtc(inFile, inputTime.Value);

            outFile = inFile + ".js";
            if (outputTime != null)
            {
                File.WriteAllText(outFile, "");
                File.SetLastWriteTimeUtc(outFile, outputTime.Value);
            }
        }

        class FakeBuildEngine : IBuildEngine
        {
            public int ColumnNumberOfTaskNode { get { return 0; } }

            public bool ContinueOnError { get { return false; } }

            public int LineNumberOfTaskNode { get { return 0; } }

            public string ProjectFileOfTaskNode { get { return "x.proj"; } }

            public bool BuildProjectFile(
                string projectFileName,
                string[] targetNames,
                System.Collections.IDictionary globalProperties,
                System.Collections.IDictionary targetOutputs)
            {
                throw new NotImplementedException();
            }

            public void LogCustomEvent(CustomBuildEventArgs e)
            {
                Console.WriteLine("C: {0}", e.Message);
            }

            public void LogErrorEvent(BuildErrorEventArgs e)
            {
                Console.WriteLine("E: {0}", e.Message);
            }

            public void LogMessageEvent(BuildMessageEventArgs e)
            {
                Console.WriteLine("M: {0}", e.Message);
            }

            public void LogWarningEvent(BuildWarningEventArgs e)
            {
                Console.WriteLine("W: {0}", e.Message);
            }
        }
    }
}
