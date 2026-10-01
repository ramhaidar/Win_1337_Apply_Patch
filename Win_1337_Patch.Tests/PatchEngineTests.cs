using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Win_1337_Patch.Tests
{
    [TestClass]
    public sealed class PatchEngineTests
    {
        private string tempDirectory;

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
            var targetFile = Path.Combine(tempDirectory, "dummy.exe");
            var patchFile = Path.Combine(tempDirectory, "dummy.1337");
            var inputBytes = new byte[] { 0x90, 0xAA, 0x90 };

            File.WriteAllBytes(targetFile, inputBytes);
            File.WriteAllLines(patchFile, new[] { ">dummy.exe", "1:AA->BB" });

            var request = new PatchRequest(patchFile, targetFile, fixFileOffset: false, createBackup: true, takeOwnership: false, skipChecksum: true);
            var outcome = PatchEngine.ApplyPatch(request);

            Assert.IsTrue(outcome.Success, outcome.Message);
            Assert.AreEqual(0xBB, File.ReadAllBytes(targetFile)[1]);
            Assert.IsFalse(string.IsNullOrEmpty(outcome.BackupPath));
            Assert.IsTrue(File.Exists(outcome.BackupPath));
            CollectionAssert.AreEqual(inputBytes, File.ReadAllBytes(outcome.BackupPath));
        }

        [TestMethod]
        public void ApplyPatchFailsWhenHeaderInvalid()
        {
            var targetFile = Path.Combine(tempDirectory, "dummy.exe");
            var patchFile = Path.Combine(tempDirectory, "dummy.1337");

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
            var targetFile = Path.Combine(tempDirectory, "dummy.exe");
            var patchFile = Path.Combine(tempDirectory, "dummy.1337");

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
            var targetFile = Path.Combine(tempDirectory, "dummy.exe");
            var patchFile = Path.Combine(tempDirectory, "dummy.1337");

            File.WriteAllBytes(targetFile, new byte[] { 0x00, 0x00 });
            File.WriteAllLines(patchFile, new[] { ">dummy.exe", "1:FF->AB" });

            var request = new PatchRequest(patchFile, targetFile, fixFileOffset: false, createBackup: false, takeOwnership: false, skipChecksum: true);
            var outcome = PatchEngine.ApplyPatch(request);

            Assert.IsFalse(outcome.Success);
            StringAssert.Contains(outcome.Message.ToLowerInvariant(), "offset");
        }

        [TestMethod]
        public void NormalizationFailurePreservesBackupAndCannotRetry()
        {
            var targetFile = Path.Combine(tempDirectory, "dummy.exe");
            var patchFile = Path.Combine(tempDirectory, "dummy.1337");
            File.WriteAllBytes(targetFile, new byte[] { 0xAA });
            File.WriteAllLines(patchFile, new[] { ">dummy.exe", "0:AA->BB" });
            var files = new TestFileOperations { NormalizationError = new UnauthorizedAccessException("Synthetic normalization denial.") };

            var outcome = PatchEngine.ApplyPatch(
                new PatchRequest(patchFile, targetFile, createBackup: true),
                new PatchExecutionContext(files, new TestPrivilegeContext(), new TestOwnershipService()));

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(PatchFailureKind.PostMutationFailure, outcome.FailureKind);
            Assert.IsTrue(outcome.TargetMayBeModified);
            Assert.IsFalse(outcome.CanRetryElevated);
            Assert.IsTrue(File.Exists(outcome.BackupPath));
            CollectionAssert.AreEqual(new byte[] { 0xAA }, File.ReadAllBytes(outcome.BackupPath));
            CollectionAssert.AreEqual(new byte[] { 0xBB }, File.ReadAllBytes(targetFile));
        }

        [TestMethod]
        public void TargetWriteDeniedIsEligibleBeforeMutation()
        {
            var targetFile = Path.Combine(tempDirectory, "dummy.exe");
            var patchFile = Path.Combine(tempDirectory, "dummy.1337");
            File.WriteAllBytes(targetFile, new byte[] { 0xAA });
            File.WriteAllLines(patchFile, new[] { ">dummy.exe", "0:AA->BB" });
            var files = new TestFileOperations { WriteAccessError = new UnauthorizedAccessException("Synthetic write denial.") };
            var outcome = PatchEngine.ApplyPatch(
                new PatchRequest(patchFile, targetFile, false, false, false, true),
                new PatchExecutionContext(files, new TestPrivilegeContext(), new TestOwnershipService()));

            Assert.AreEqual(PatchFailureKind.AccessDenied, outcome.FailureKind);
            Assert.AreEqual(PatchStage.TargetWrite, outcome.Stage);
            Assert.IsTrue(outcome.CanRetryElevated);
            Assert.IsFalse(outcome.TargetMayBeModified);
            CollectionAssert.AreEqual(new byte[] { 0xAA }, File.ReadAllBytes(targetFile));
        }

        [TestMethod]
        public void BackupAccessDenialPreservesCauseAndDoesNotWrite()
        {
            var (patch, target) = CreateValidFixture();
            var files = new TestFileOperations { BackupError = new UnauthorizedAccessException("Synthetic backup denial.") };
            var outcome = ApplyFixture(patch, target, files, createBackup: true);
            Assert.AreEqual(PatchStage.Backup, outcome.Stage);
            Assert.IsInstanceOfType<UnauthorizedAccessException>(outcome.Error);
            Assert.IsTrue(outcome.CanRetryElevated);
            Assert.IsFalse(outcome.TargetMayBeModified);
            CollectionAssert.AreEqual(new byte[] { 0xAA }, File.ReadAllBytes(target));
        }

        [TestMethod]
        public void SharingViolationDoesNotRequestElevation()
        {
            var (patch, target) = CreateValidFixture();
            var outcome = ApplyFixture(patch, target,
                new TestFileOperations { WriteAccessError = new IOException("Synthetic sharing violation.") });
            Assert.AreEqual(PatchFailureKind.IoFailure, outcome.FailureKind);
            Assert.IsFalse(outcome.CanRetryElevated);
            Assert.IsFalse(outcome.TargetMayBeModified);
        }

        [TestMethod]
        public void PatchReadDeniedDoesNotRequestElevation()
        {
            var (patch, target) = CreateValidFixture();
            var outcome = ApplyFixture(patch, target,
                new TestFileOperations { PatchReadError = new UnauthorizedAccessException("Synthetic patch-read denial.") });
            Assert.AreEqual(PatchStage.PatchRead, outcome.Stage);
            Assert.IsFalse(outcome.CanRetryElevated);
            Assert.IsFalse(outcome.TargetMayBeModified);
        }

        [TestMethod]
        public void TargetReadDeniedDoesNotMutate()
        {
            var (patch, target) = CreateValidFixture();
            var outcome = ApplyFixture(patch, target,
                new TestFileOperations { ReadAccessError = new UnauthorizedAccessException("Synthetic read denial.") });
            Assert.AreEqual(PatchStage.TargetRead, outcome.Stage);
            Assert.IsTrue(outcome.CanRetryElevated);
            Assert.IsFalse(outcome.TargetMayBeModified);
            CollectionAssert.AreEqual(new byte[] { 0xAA }, File.ReadAllBytes(target));
        }

        [TestMethod]
        public void FailureAfterWriteStartsCannotRetry()
        {
            var (patch, target) = CreateValidFixture();
            var outcome = ApplyFixture(patch, target, new TestFileOperations { FailDuringWrite = true }, createBackup: true);
            Assert.AreEqual(PatchFailureKind.PostMutationFailure, outcome.FailureKind);
            Assert.IsFalse(outcome.CanRetryElevated);
            Assert.IsTrue(outcome.TargetMayBeModified);
            CollectionAssert.AreEqual(new byte[] { 0xAA }, File.ReadAllBytes(outcome.BackupPath));
        }

        [TestMethod]
        [DataRow("invalid", "0:AA->BB")]
        [DataRow(">dummy.exe", "0:FF->BB")]
        [DataRow(">dummy.exe", "2:AA->BB")]
        [DataRow(">dummy.exe", "0:AA->BB\ninvalid")]
        public void InvalidPatchNeverChangesOwnership(string header, string entry)
        {
            var (patch, target) = CreateValidFixture();
            File.WriteAllText(patch, header + "\n" + entry);
            var outcome = PatchEngine.ApplyPatch(new PatchRequest(patch, target, false, false, true, true),
                new PatchExecutionContext(new TestFileOperations(), new TestPrivilegeContext(), new TestOwnershipService()));
            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(PatchFailureKind.Validation, outcome.FailureKind);
            Assert.IsFalse(outcome.CanRetryElevated);
            CollectionAssert.AreEqual(new byte[] { 0xAA }, File.ReadAllBytes(target));
        }

        [TestMethod]
        public void TargetChangedBeforeWriteIsRevalidated()
        {
            var (patch, target) = CreateValidFixture();
            var files = new TestFileOperations { BeforeWriteOpen = () => File.WriteAllBytes(target, new byte[] { 0xCC }) };
            var outcome = ApplyFixture(patch, target, files);
            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(PatchFailureKind.Validation, outcome.FailureKind);
            Assert.IsFalse(outcome.TargetMayBeModified);
            CollectionAssert.AreEqual(new byte[] { 0xCC }, File.ReadAllBytes(target));
        }

        private (string Patch, string Target) CreateValidFixture()
        {
            var target = Path.Combine(tempDirectory, "dummy.exe");
            var patch = Path.Combine(tempDirectory, "dummy.1337");
            File.WriteAllBytes(target, new byte[] { 0xAA });
            File.WriteAllLines(patch, new[] { ">dummy.exe", "0:AA->BB" });
            return (patch, target);
        }

        [TestMethod]
        public void ElevatedDeniedWriteUsesAuthorizedOwnershipOnce()
        {
            var (patch, target) = CreateValidFixture();
            var files = new TestFileOperations { WriteAccessError = new UnauthorizedAccessException("Denied.") };
            var ownership = new CallbackOwnershipService(() => files.WriteAccessError = null);
            var result = PatchEngine.ApplyPatch(new PatchRequest(patch, target, false, false, true, true),
                new PatchExecutionContext(files, new TestPrivilegeContext(true), ownership));
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(1, ownership.Calls);
            CollectionAssert.AreEqual(new byte[] { 0xBB }, File.ReadAllBytes(target));
        }

        [TestMethod]
        public void OwnershipRetryRevalidatesChangedTarget()
        {
            var (patch, target) = CreateValidFixture();
            var files = new TestFileOperations { WriteAccessError = new UnauthorizedAccessException("Denied.") };
            var ownership = new CallbackOwnershipService(() =>
            {
                files.WriteAccessError = null;
                File.WriteAllBytes(target, new byte[] { 0xCC });
            });
            var result = PatchEngine.ApplyPatch(new PatchRequest(patch, target, false, false, true, true),
                new PatchExecutionContext(files, new TestPrivilegeContext(true), ownership));
            Assert.AreEqual(1, ownership.Calls);
            Assert.AreEqual(PatchFailureKind.Validation, result.FailureKind);
            CollectionAssert.AreEqual(new byte[] { 0xCC }, File.ReadAllBytes(target));
        }

        [TestMethod]
        [DataRow(false, true, "write")]
        [DataRow(true, false, "write")]
        [DataRow(true, true, "backup")]
        [DataRow(true, true, "read")]
        [DataRow(true, true, "none")]
        public void OwnershipRequiresElevatedValidatedDeniedWrite(bool elevated, bool requested, string deniedStage)
        {
            var (patch, target) = CreateValidFixture();
            var denial = new UnauthorizedAccessException("Synthetic denial.");
            var files = new TestFileOperations
            {
                WriteAccessError = deniedStage == "write" ? denial : null,
                ReadAccessError = deniedStage == "read" ? denial : null,
                BackupError = deniedStage == "backup" ? denial : null
            };
            var result = PatchEngine.ApplyPatch(new PatchRequest(patch, target, false, true, requested, true),
                new PatchExecutionContext(files, new TestPrivilegeContext(elevated), new TestOwnershipService()));
            Assert.AreEqual(deniedStage == "none", result.Success);
        }

        [TestMethod]
        public void OwnershipFailureStopsPatchWrite()
        {
            var (patch, target) = CreateValidFixture();
            var ownership = new CallbackOwnershipService(() => { }, fail: true);
            var result = PatchEngine.ApplyPatch(new PatchRequest(patch, target, false, false, true, true),
                new PatchExecutionContext(new TestFileOperations { WriteAccessError = new UnauthorizedAccessException() },
                    new TestPrivilegeContext(true), ownership));
            Assert.AreEqual(PatchStage.Ownership, result.Stage);
            Assert.IsFalse(result.CanRetryElevated);
            CollectionAssert.AreEqual(new byte[] { 0xAA }, File.ReadAllBytes(target));
        }

        private static PatchOutcome ApplyFixture(string patch, string target, TestFileOperations files, bool createBackup = false)
        {
            return PatchEngine.ApplyPatch(new PatchRequest(patch, target, false, createBackup, false, true),
                new PatchExecutionContext(files, new TestPrivilegeContext(), new TestOwnershipService()));
        }

        private sealed class TestPrivilegeContext : IPrivilegeContext
        {
            public TestPrivilegeContext(bool elevated = false) { IsElevated = elevated; }
            public bool IsElevated { get; }
        }

        private sealed class CallbackOwnershipService : IFileOwnershipService
        {
            private readonly Action callback;
            private readonly bool fail;
            public int Calls { get; private set; }
            public CallbackOwnershipService(Action callback, bool fail = false)
            {
                this.callback = callback;
                this.fail = fail;
            }
            public PatchOutcome Grant(string targetPath, Action<string> log = null)
            {
                Calls++;
                callback();
                return fail ? PatchOutcome.Failure("Synthetic ownership failure.") : PatchOutcome.SuccessOutcome("Granted.");
            }
        }

        private sealed class TestOwnershipService : IFileOwnershipService
        {
            public PatchOutcome Grant(string targetPath, Action<string> log = null)
            {
                Assert.Fail("Ownership was not authorized for this test.");
                return null;
            }
        }

        private sealed class TestFileOperations : IPatchFileOperations
        {
            public Exception WriteAccessError { get; set; }
            public Exception NormalizationError { get; set; }
            public Exception ReadAccessError { get; set; }
            public Exception PatchReadError { get; set; }
            public Exception BackupError { get; set; }
            public Action BeforeWriteOpen { get; set; }
            public bool FailDuringWrite { get; set; }
            public string[] ReadPatchLines(string path)
            {
                if (PatchReadError != null)
                    throw PatchReadError;
                return File.ReadAllLines(path);
            }

            public Stream OpenTarget(string path, FileAccess access)
            {
                if (access == FileAccess.ReadWrite && WriteAccessError != null)
                    throw WriteAccessError;
                if (access == FileAccess.Read && ReadAccessError != null)
                    throw ReadAccessError;
                if (access == FileAccess.ReadWrite)
                {
                    BeforeWriteOpen?.Invoke();
                    if (FailDuringWrite)
                        return new FailingWriteStream(File.ReadAllBytes(path));
                }
                return new FileStream(path, FileMode.Open, access, FileShare.None);
            }

            public string CreateBackup(string targetPath, Stream original, Action<string> log)
            {
                if (BackupError != null)
                    throw BackupError;
                var path = targetPath + ".test.BAK";
                var position = original.Position;
                original.Position = 0;
                using (var backup = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    original.CopyTo(backup);
                original.Position = position;
                return path;
            }

            public void Normalize(string path, Action<string> log)
            {
                if (NormalizationError != null)
                    throw NormalizationError;
            }
        }

        private sealed class FailingWriteStream : MemoryStream
        {
            public FailingWriteStream(byte[] bytes) : base(bytes) { }
            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new UnauthorizedAccessException("Synthetic failure after a write was attempted.");
            }
        }
    }
}
