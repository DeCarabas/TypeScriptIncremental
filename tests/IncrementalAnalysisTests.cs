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
                new { paths = new[] { @"c:\a\b.ts", @"c:\a\c.ts" },               common = @"c:\a\" },
                new { paths = new[] { @"c:\a\b.ts", @"c:\a\c.ts", @"c:\a\d.ts" }, common = @"c:\a\" },
                new { paths = new[] { @"c:\a\b\c.ts", @"c:\a\b\d.ts" },           common = @"c:\a\b\" },
                new { paths = new[] { @"c:\a\b\c.ts", @"c:\a.ts" },               common = @"c:\" },
                new { paths = new[] { @"c:\a\b.ts", @"c:\c\d.ts" },               common = @"c:\" },
                new { paths = new[] { @"c:\a\b.ts", @"d:\a\b.ts" },               common = (string)null },
                new { paths = new[] { @"c:\a\b.ts", @"d:\e\f.ts" },               common = (string)null },
                new { paths = new[] { @"c:\a\b.ts", @"d:\e\f.d.ts" },             common = @"c:\a\" },
                new { paths = new[] { @"c:\a\b\c.ts", @"c:\a\d.ts" },             common = @"c:\a\" },
            };

            for (int i = 0; i < cases.Length; i++)
            {
                Assert.AreEqual(
                    cases[i].common,
                    IncrementalAnalysis.ComputeCommonDirectoryPath(cases[i].paths), "Case {0}.0", i);

                ITaskItem[] items = cases[i].paths.Select(p => CreateTaskItem(p)).ToArray();
                Assert.AreEqual(
                    cases[i].common,
                    IncrementalAnalysis.ComputeCommonDirectoryPath(items), "Case {0}.1", i);
            }
        }

        [TestMethod]
        public void DescribeComputeOutputFile()
        {
            var cases = new[] 
            {
                new { file= @"c:\a\b.ts",     common = @"c:\a\",      outdir = @"d:\", result = @"d:\b.js" },
                new { file= @"c:\a\b\c.ts",   common = @"c:\a\",      outdir = @"d:\", result = @"d:\b\c.js" },
                new { file= @"c:\a\b\c.ts",   common = (string)null,  outdir = @"d:\", result = @"c:\a\b\c.js" },
                new { file= @"c:\a\b\c.d.ts", common = (string)null,  outdir = @"d:\", result = (string)null },
            };

            for (int i = 0; i < cases.Length; i++)
            {
                Assert.AreEqual(
                    cases[i].result,
                    IncrementalAnalysis.ComputeOutputFile(cases[i].file, cases[i].common, cases[i].outdir));
            }
        }

        [TestMethod]
        public void DescribeComputeOutputMapping()
        {
            var cases = new[] 
            {
                new { 
                    paths = new[] { @"c:\a\b.ts", @"c:\a\c.ts" },               
                    outdir = (string)null, 
                    outputs = new[] { @"c:\a\b.js", @"c:\a\c.js" },
                },
                new { 
                    paths = new[] { @"c:\a\b.ts", @"c:\a\c.ts", @"c:\a\d\e.ts" }, 
                    outdir = @"d:\temp\", 
                    outputs = new[] { @"d:\temp\b.js", @"d:\temp\c.js", @"d:\temp\d\e.js" },
                },
                new { 
                    paths = new[] { @"c:\a\b.ts", @"d:\a\b.ts" },               
                    outdir = @"d:\temp\", 
                    outputs= new[] {@"c:\a\b.js", @"d:\a\b.js" },
                },
            };

            for (int i = 0; i < cases.Length; i++)
            {
                ITaskItem[] items = cases[i].paths.Select(p => CreateTaskItem(p)).ToArray();

                IDictionary<string, string> mapping;
                mapping = IncrementalAnalysis.ComputeOutputMapping(items, cases[i].outdir);

                for (int j = 0; j < cases[i].paths.Length; j++)
                {
                    string infile = cases[i].paths[j];
                    string outfile = cases[i].outputs[j];

                    if (!mapping.ContainsKey(infile))
                    {
                        Assert.Fail("Case {0}: Output mapping does not have a mapping for file {1}", i, infile);
                    }

                    Assert.AreEqual(
                        outfile, mapping[infile], "Case {0}: Output file did not match for input {1}", i, infile);
                }
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

        [TestMethod]
        public void DescribeLoadCache()
        {
            string lockedPath = null;
            FileStream stream = null;
            DependencyCache cache = null;

            XSpec
                .Given("A file path that doesn't exist and a file path that is locked", () =>
                {
                    lockedPath = Path.GetTempFileName();
                    stream = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);
                })
                .When("I load the cache with the locked file",
                    () => cache = IncrementalAnalysis.LoadDependencyCache(lockedPath, CreateFakeLog()))
                    .It("returns an empty cache", () => Assert.AreEqual(0, cache.Count))
                .When("I load the cache with a null file",
                    () => cache = IncrementalAnalysis.LoadDependencyCache(null, CreateFakeLog()))
                    .It("returns an empty cache", () => Assert.AreEqual(0, cache.Count))
                .When("I load the cache with an empty file",
                    () => cache = IncrementalAnalysis.LoadDependencyCache("", CreateFakeLog()))
                    .It("returns an empty cache", () => Assert.AreEqual(0, cache.Count))
                .When("I load the cache with a non-existant file",
                    () => cache = IncrementalAnalysis.LoadDependencyCache(Guid.NewGuid().ToString(), CreateFakeLog()))
                    .It("returns an empty cache", () => Assert.AreEqual(0, cache.Count))
            .Go();
        }

        [TestMethod]
        public void DescribeSaveCache()
        {
            string lockedPath = null;
            FileStream stream = null;
            DependencyCache cache = null;

            // All these should just work; no asserts needed.
            lockedPath = Path.GetTempFileName();
            stream = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);
            cache = new DependencyCache() { IsModified = true };


            IncrementalAnalysis.SaveDependencyCache(cache, null, CreateFakeLog());
            IncrementalAnalysis.SaveDependencyCache(cache, lockedPath, CreateFakeLog());
            IncrementalAnalysis.SaveDependencyCache(cache, "", CreateFakeLog());
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
