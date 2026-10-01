using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Win_1337_Patch.Tests
{
    [TestClass]
    public sealed class FileOwnershipServiceTests
    {
        [TestMethod]
        public void OwnershipUtilitiesReceiveLiteralTargetArguments()
        {
            var runner = new RecordingRunner();
            var service = new FileOwnershipService(runner, @"C:\Windows\System32");
            var target = @"C:\test folder\a&b%name(1).dll";
            var result = service.Grant(target);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(@"C:\Windows\System32\takeown.exe", runner.Calls[0].Path);
            CollectionAssert.AreEqual(new[] { "/F", target }, runner.Calls[0].Arguments);
            Assert.AreEqual(@"C:\Windows\System32\icacls.exe", runner.Calls[1].Path);
            CollectionAssert.AreEqual(new[] { target, "/grant", "*S-1-5-32-544:F" }, runner.Calls[1].Arguments);
        }

        [TestMethod]
        public void TakeownFailureStopsBeforeIcacls()
        {
            var runner = new RecordingRunner { FailCall = 1 };
            var result = new FileOwnershipService(runner, @"C:\Windows\System32").Grant(@"C:\test.dll");
            Assert.IsFalse(result.Success);
            Assert.AreEqual(PatchStage.Ownership, result.Stage);
            Assert.AreEqual(1, runner.Calls.Count);
        }

        [TestMethod]
        public void IcaclsFailureReportsPartialPermissionChange()
        {
            var result = new FileOwnershipService(new RecordingRunner { FailCall = 2 }, @"C:\Windows\System32")
                .Grant(@"C:\test.dll");
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message.ToLowerInvariant(), "ownership may have changed");
        }

        private sealed class RecordingRunner : IOwnershipCommandRunner
        {
            public List<(string Path, string[] Arguments)> Calls { get; } = new List<(string, string[])>();
            public int FailCall { get; set; }
            public int Run(string executablePath, IReadOnlyList<string> arguments, Action<string> log)
            {
                Calls.Add((executablePath, arguments.ToArray()));
                return Calls.Count == FailCall ? 5 : 0;
            }
        }
    }
}
