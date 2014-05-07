using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TypeScript.Tasks.Tests
{
    [TestClass]
    public class AssemblyInit
    {
        // Make sure that the compiler keeps this; so mstest will deploy our appropriate assemblies.
        public static VsTsc DummyReference = new VsTsc();

        [AssemblyInitialize]
        public static void AssemblyInitialize( TestContext context )
        {
        }
    }
}
