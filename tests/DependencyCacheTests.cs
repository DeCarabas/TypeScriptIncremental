namespace TypeScript.Tasks.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using Doty.Spec;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class CacheTests
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
        public void DescribeDependencyCache()
        {
            string baseFile = null;
            string depFile = null;
            DependencyCache cache = null;
            DateTime touchTime = new DateTime();
            StringBuilder buffer = null;
            DateTime baseEMT = new DateTime();
            DateTime depEMT = new DateTime();

            XSpec.Given("Two files, one of which depends on the other", () =>
                {
                    baseFile = CreateTestFile();
                    Thread.Sleep(TimeSpan.FromMilliseconds(1));
                    depFile = CreateTestFile(baseFile);

                    buffer = new StringBuilder();
                })
                .When("I create an empty cache", () => cache = new DependencyCache())
                    .It("is not modified", () => Assert.IsFalse(cache.IsModified))
                    .It("has no items", () => Assert.AreEqual(0, cache.Count))

                .When("I save the cache", () => cache.Write(new StringWriter(buffer)))
                .When("I load that cache", () => cache = new DependencyCache(new StringReader(buffer.ToString())))
                    .It("is not modified", () => Assert.IsFalse(cache.IsModified))
                    .It("has no items", () => Assert.AreEqual(0, cache.Count))

                .When("I create a cache and get the EMTs", () =>
                    {
                        cache = new DependencyCache();
                        baseEMT = cache.GetEffectiveModifiedTime(baseFile);
                        depEMT = cache.GetEffectiveModifiedTime(depFile);
                    })
                    .It("has the same EMT and Last Write for the base",
                        () => Assert.AreEqual(File.GetLastWriteTimeUtc(baseFile), baseEMT))
                    .It("has the same EMT and Last Write for the dependent file",
                        () => Assert.AreEqual(File.GetLastWriteTimeUtc(depFile), depEMT))
                    .It("has different write times for the two different files",
                        () => Assert.AreNotEqual(baseEMT, depEMT))
                    .It("is tracking both files", () => Assert.AreEqual(2, cache.Count))

                .When("I clear the buffer and save the cache", () =>
                    {
                        buffer.Clear();
                        cache.Write(new StringWriter(buffer));
                    })
                .When("I load the cache and get the EMTs again", () =>
                    {
                        cache = new DependencyCache(new StringReader(buffer.ToString()));
                        baseEMT = cache.GetEffectiveModifiedTime(baseFile);
                        depEMT = cache.GetEffectiveModifiedTime(depFile);
                    })
                    .It("has two items, still", () => Assert.AreEqual(2, cache.Count))
                    .It("is unmodified", () => Assert.IsFalse(cache.IsModified))
                    .It("still gives the right EMT for base",
                        () => Assert.AreEqual(File.GetLastWriteTimeUtc(baseFile), baseEMT))
                    .It("still gives the right EMT for dep",
                        () => Assert.AreEqual(File.GetLastWriteTimeUtc(depFile), depEMT))

                .When("I set the write time for the base",
                    () => File.SetLastWriteTimeUtc(baseFile, (touchTime = DateTime.UtcNow.AddHours(1))))

                .When("I rebuild the cache", () => cache = new DependencyCache())
                    .It("reports the write time for the base as the new write time",
                        () => Assert.AreEqual(touchTime, cache.GetEffectiveModifiedTime(baseFile)))
                    .It("has the same EMT for base and dependent",
                        () => Assert.AreEqual(
                            cache.GetEffectiveModifiedTime(baseFile), cache.GetEffectiveModifiedTime(depFile)))

            .Go();
        }

        string CreateTestFile(params string[] dependencies)
        {
            string file = Path.Combine(this.baseDir, Path.GetRandomFileName() + ".xx.ts");

            using (var writer = File.CreateText(file))
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

            return file;
        }
    }
}
