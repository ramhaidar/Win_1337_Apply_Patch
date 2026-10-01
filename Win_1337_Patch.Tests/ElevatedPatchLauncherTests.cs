using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Win_1337_Patch.Tests
{
    [TestClass]
    public sealed class ElevatedPatchLauncherTests
    {
        [TestMethod]
        public async Task WorkerLaunchHasExactExecutableOptionsAndLiteralPaths()
        {
            var runner = new Runner();
            var launcher = new ElevatedPatchLauncher(runner, @"C:\app folder\patcher.exe");
            var result = await launcher.LaunchAsync(new PatchRequest(@"C:\patch folder\a&b.1337",
                @"C:\target folder\target.dll", true, true, true));
            Assert.IsTrue(result.Success);
            Assert.AreEqual(@"C:\app folder\patcher.exe", runner.Info.FileName);
            Assert.AreEqual("runas", runner.Info.Verb);
            Assert.IsTrue(runner.Info.UseShellExecute);
            Assert.IsFalse(runner.Info.RedirectStandardOutput);
            Assert.AreEqual("-patch \"C:\\patch folder\\a&b.1337\" \"C:\\target folder\\target.dll\" -fileoffset -backup -takeownership -elevatedworker",
                runner.Info.Arguments);
        }

        [TestMethod]
        public async Task WorkerArgumentsContainOnlySelectedOptions()
        {
            var runner = new Runner();
            await new ElevatedPatchLauncher(runner, @"C:\patcher.exe").LaunchAsync(
                new PatchRequest(@"C:\a.1337", @"C:\b.dll"));
            Assert.AreEqual("-patch \"C:\\a.1337\" \"C:\\b.dll\" -elevatedworker", runner.Info.Arguments);
        }

        [TestMethod]
        [DataRow("", "\"\"")]
        [DataRow("a b", "\"a b\"")]
        [DataRow("a\"b", "\"a\\\"b\"")]
        [DataRow("C:\\folder\\", "\"C:\\folder\\\\\"")]
        [DataRow("a\\\"b", "\"a\\\\\\\"b\"")]
        public void QuotesWindowsArgumentsWithoutLosingBackslashes(string input, string expected)
        {
            Assert.AreEqual(expected, ElevatedPatchLauncher.QuoteWindowsArgument(input));
        }

        [TestMethod]
        public async Task CancelledUacIsFailure()
        {
            var result = await new ElevatedPatchLauncher(new Runner { Error = new Win32Exception(1223) }, @"C:\patcher.exe")
                .LaunchAsync(new PatchRequest(@"C:\a.1337", @"C:\b.dll"));
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message.ToLowerInvariant(), "cancel");
        }

        [TestMethod]
        [DataRow(0, true)]
        [DataRow(1, false)]
        public async Task ChildExitStatusControlsSuccess(int exitCode, bool expectedSuccess)
        {
            var result = await new ElevatedPatchLauncher(new Runner { ExitCode = exitCode }, @"C:\patcher.exe")
                .LaunchAsync(new PatchRequest(@"C:\a.1337", @"C:\b.dll"));
            Assert.AreEqual(expectedSuccess, result.Success);
            Assert.IsNull(result.BackupPath);
        }

        [TestMethod]
        public async Task LaunchErrorIsNotSuccess()
        {
            var result = await new ElevatedPatchLauncher(new Runner { Error = new Win32Exception(2) }, @"C:\patcher.exe")
                .LaunchAsync(new PatchRequest(@"C:\a.1337", @"C:\b.dll"));
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public void WorkerArgumentsRoundTripThroughWindowsAndConsoleParser()
        {
            var request = new PatchRequest(@"C:\patch folder\a&b%(1).1337", @"C:\target folder\target.dll", true, true, true);
            var tokens = SplitWindowsCommandLine("\"C:\\patcher.exe\" " + ElevatedPatchLauncher.BuildArguments(request));
            var parser = new Program.ConsolePatchParser(tokens.Skip(1).ToArray());

            Assert.IsTrue(parser.IsValid, parser.ErrorMessage);
            Assert.AreEqual(@"C:\patch folder\a&b%(1).1337", parser.PatchFilePath);
            Assert.AreEqual(@"C:\target folder\target.dll", parser.TargetFilePath);
            Assert.IsTrue(parser.FixOffset);
            Assert.IsTrue(parser.CreateBackup);
            Assert.IsTrue(parser.TakeOwnership);
            Assert.IsTrue(parser.ElevatedWorker);
            Assert.IsFalse(parser.Elevate);
            Assert.IsFalse(parser.ScheduleOnNextBoot);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("a\"b")]
        [DataRow("a\\\"b")]
        [DataRow("C:\\folder\\")]
        [DataRow("a&b%(1) space")]
        public void QuotedArgumentRoundTripsThroughWindows(string value)
        {
            var tokens = SplitWindowsCommandLine("patcher.exe " + ElevatedPatchLauncher.QuoteWindowsArgument(value));
            Assert.AreEqual(2, tokens.Length);
            Assert.AreEqual(value, tokens[1]);
        }

        private static string[] SplitWindowsCommandLine(string commandLine)
        {
            var arguments = CommandLineToArgvW(commandLine, out int count);
            Assert.AreNotEqual(IntPtr.Zero, arguments, "Windows must parse the generated command line.");
            try
            {
                var result = new string[count];
                for (int index = 0; index < count; index++)
                    result[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(arguments, index * IntPtr.Size));
                return result;
            }
            finally
            {
                LocalFree(arguments);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private sealed class Runner : IElevatedProcessRunner
        {
            public ProcessStartInfo Info { get; private set; }
            public Exception Error { get; set; }
            public int ExitCode { get; set; }
            public Task<int> RunAsync(ProcessStartInfo startInfo)
            {
                Info = startInfo;
                return Error != null ? Task.FromException<int>(Error) : Task.FromResult(ExitCode);
            }
        }
    }
}
