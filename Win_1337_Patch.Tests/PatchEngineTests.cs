using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Win_1337_Patch.Tests
{
    [TestClass]
    public sealed class PatchEngineTests
    {
        private string? tempDirectory;

        [TestInitialize]
        public void Initialize()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (tempDirectory != null && Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }

        [TestMethod]
        public void ApplyPatchCreatesBackupAndUpdatesBytes()
        {
            var targetFile = Path.Combine(tempDirectory!, "dummy.exe");
            var patchFile = Path.Combine(tempDirectory!, "dummy.1337");
            var inputBytes = new byte[] { 0x90, 0xAA, 0x90 };

            File.WriteAllBytes(targetFile, inputBytes);
            File.WriteAllLines(patchFile, new[] { ">dummy.exe", "1:AA->BB" });

            var request = new PatchRequest(patchFile, targetFile, fixFileOffset: false, createBackup: true, takeOwnership: false, skipChecksum: true);
            var outcome = PatchEngine.ApplyPatch(request);

            Assert.IsTrue(outcome.Success, outcome.Message);
            Assert.AreEqual(0xBB, File.ReadAllBytes(targetFile)[1]);
            Assert.IsFalse(string.IsNullOrEmpty(outcome.BackupPath));
            Assert.IsTrue(File.Exists(outcome.BackupPath));
            CollectionAssert.AreEqual(inputBytes, File.ReadAllBytes(outcome.BackupPath!));
        }

        [TestMethod]
        public void ApplyPatchFailsWhenHeaderInvalid()
        {
            var targetFile = Path.Combine(tempDirectory!, "dummy.exe");
            var patchFile = Path.Combine(tempDirectory!, "dummy.1337");

            File.WriteAllBytes(targetFile, new byte[] { 0x00 });
            File.WriteAllLines(patchFile, new[] { "dummy.exe", "0:00->01" });

            var request = new PatchRequest(patchFile, targetFile, fixFileOffset: false, createBackup: false, takeOwnership: false, skipChecksum: true);
            var outcome = PatchEngine.ApplyPatch(request);

            Assert.IsFalse(outcome.Success);
            StringAssert.Contains(outcome.Message.ToLowerInvariant(), "header");
        }

        [TestMethod]
        public void ApplyPatchFailsWhenTargetNameMismatch()
        {
            var targetFile = Path.Combine(tempDirectory!, "dummy.exe");
            var patchFile = Path.Combine(tempDirectory!, "dummy.1337");

            File.WriteAllBytes(targetFile, new byte[] { 0x00 });
            File.WriteAllLines(patchFile, new[] { ">other.exe", "0:00->01" });

            var request = new PatchRequest(patchFile, targetFile, fixFileOffset: false, createBackup: false, takeOwnership: false, skipChecksum: true);
            var outcome = PatchEngine.ApplyPatch(request);

            Assert.IsFalse(outcome.Success);
            StringAssert.Contains(outcome.Message.ToLowerInvariant(), "not valid");
        }

        [TestMethod]
        public void ApplyPatchFailsWhenByteDoesNotMatch()
        {
            var targetFile = Path.Combine(tempDirectory!, "dummy.exe");
            var patchFile = Path.Combine(tempDirectory!, "dummy.1337");

            File.WriteAllBytes(targetFile, new byte[] { 0x00, 0x00 });
            File.WriteAllLines(patchFile, new[] { ">dummy.exe", "1:FF->AB" });

            var request = new PatchRequest(patchFile, targetFile, fixFileOffset: false, createBackup: false, takeOwnership: false, skipChecksum: true);
            var outcome = PatchEngine.ApplyPatch(request);

            Assert.IsFalse(outcome.Success);
            StringAssert.Contains(outcome.Message.ToLowerInvariant(), "offset");
        }
    }
}
