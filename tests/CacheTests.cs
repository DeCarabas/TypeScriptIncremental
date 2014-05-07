using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TypeScript.Tasks.Tests
{
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
        public void EmptyCacheSaveLoad()
        {
            var cache = new DependencyCache();

            var writer = new StringWriter();
            cache.Write(writer);

            var reader = new StringReader(writer.ToString());
            var newCache = new DependencyCache(reader);

            Assert.IsFalse(newCache.IsModified, "Cache should not be modified.");
        }

        [TestMethod]
        public void CacheRecordSaveLoad()
        {
            var cache = new DependencyCache();

            string file = CreateTestFile();
            DateTime emt = cache.GetEffectiveModifiedTime(file);
            Assert.IsTrue(cache.IsModified, "Cache should be modified.");

            var writer = new StringWriter();
            cache.Write(writer);

            var reader = new StringReader(writer.ToString());
            var newCache = new DependencyCache(reader);
            Assert.IsFalse(newCache.IsModified, "Cache should not be modified.");

            DateTime emt2 = cache.GetEffectiveModifiedTime(file);
            Assert.AreEqual(emt, emt2, "EMTs should have been equal");
        }

        [TestMethod]
        public void EmtTracksDependencies()
        {
            string baseFile = CreateTestFile();
            string depFile = CreateTestFile(baseFile);

            DateTime baseWrite = File.GetLastWriteTimeUtc(baseFile);
            DateTime depWrite = File.GetLastWriteTimeUtc(depFile);

            var cache = new DependencyCache();
            DateTime baseEMT = cache.GetEffectiveModifiedTime(baseFile);
            DateTime depEMT = cache.GetEffectiveModifiedTime(depFile);

            Assert.AreEqual(baseWrite, baseEMT, "EMT and Last Write should be the same for base");
            Assert.AreEqual(depWrite, depEMT, "EMT and Last Write should be the same for dep, too");
            Assert.IsTrue(depWrite > baseWrite, "Dependent write should be after base write");
            

            DateTime newWriteTime = DateTime.UtcNow + TimeSpan.FromHours(1);
            File.SetLastWriteTimeUtc(baseFile, newWriteTime);

            cache = new DependencyCache();
            baseEMT = cache.GetEffectiveModifiedTime(baseFile);
            depEMT = cache.GetEffectiveModifiedTime(depFile);

            Assert.AreEqual(newWriteTime, baseEMT, "Base file's EMT should have changed directly.");
            Assert.AreEqual(newWriteTime, depEMT, "Dependent file's EMT should have changed indirectly.");
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
