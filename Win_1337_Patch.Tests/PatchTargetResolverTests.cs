using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win_1337_Patch;

namespace Win_1337_Patch.Tests
{
    [TestClass]
    public sealed class PatchTargetResolverTests
    {
        [TestMethod]
        public void ReturnsAutoTargetForNvEncodeApiPatch()
        {
            const string patchFileName = "nvencodeapi.1337";
            string patchPath = Path.Combine("C:\\patches", patchFileName);

            bool result = PatchTargetResolver.TryGetAutoTarget(patchPath, out string targetPath);

            Assert.IsTrue(result);
            string expectedWindows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string expectedPath = Path.Combine(expectedWindows, "SysWOW64", "nvEncodeAPI.dll");
            Assert.AreEqual(expectedPath, targetPath);
        }

        [TestMethod]
        public void ReturnsAutoTargetForNvEncodeApi64Patch()
        {
            const string patchFileName = "nvencodeapi64.1337";
            string patchPath = Path.Combine("C:\\patches", patchFileName);

            bool result = PatchTargetResolver.TryGetAutoTarget(patchPath, out string targetPath);

            Assert.IsTrue(result);
            string expectedWindows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string expectedPath = Path.Combine(expectedWindows, "System32", "nvEncodeAPI64.dll");
            Assert.AreEqual(expectedPath, targetPath);
        }

        [TestMethod]
        public void ReturnsFalseForOtherPatchFileNames()
        {
            bool result = PatchTargetResolver.TryGetAutoTarget(Path.Combine("C:\\patches", "other.1337"), out _);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ReturnsFalseWhenPatchPathIsEmpty()
        {
            bool result = PatchTargetResolver.TryGetAutoTarget(string.Empty, out _);

            Assert.IsFalse(result);
        }
    }
}
