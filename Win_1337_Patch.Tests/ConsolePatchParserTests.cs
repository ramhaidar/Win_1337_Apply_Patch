using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Forms;
using Win_1337_Patch;
using System;
using System.Threading.Tasks;

namespace Win_1337_Patch.Tests
{
    [TestClass]
    public sealed class ConsolePatchParserTests
    {
        [TestMethod]
        public void RecognizesScheduleFlag()
        {
            var parser = new Program.ConsolePatchParser(new[] { "-patch", "driver.1337", "driver.dll", "-schedule" });

            Assert.IsTrue(parser.IsValid);
            Assert.IsTrue(parser.ScheduleOnNextBoot);
            Assert.IsFalse(parser.ScheduledRun);
        }

        [TestMethod]
        public void RecognizesScheduledRunFlag()
        {
            var parser = new Program.ConsolePatchParser(new[] { "-patch", "driver.1337", "driver.dll", "--scheduled-run" });

            Assert.IsTrue(parser.IsValid);
            Assert.IsTrue(parser.ScheduledRun);
        }

        [TestMethod]
        public void ElevationDefaultsOff()
        {
            var parser = new Program.ConsolePatchParser(new[] { "driver.1337", "driver.dll", "-takeownership", "-scheduledrun" });
            Assert.IsTrue(parser.IsValid);
            Assert.IsFalse(parser.Elevate);
            Assert.IsFalse(parser.ElevatedWorker);
        }

        [TestMethod]
        [DataRow("-elevate")]
        [DataRow("--elevate")]
        public void RecognizesElevationAliases(string option)
        {
            var parser = new Program.ConsolePatchParser(new[] { "driver.1337", "driver.dll", option });
            Assert.IsTrue(parser.IsValid);
            Assert.IsTrue(parser.Elevate);
        }

        [TestMethod]
        public void WorkerCannotSchedule()
        {
            var parser = new Program.ConsolePatchParser(new[] { "driver.1337", "driver.dll", "-elevatedworker", "-schedule" });
            Assert.IsFalse(parser.IsValid);
        }

        [TestMethod]
        public async Task ScheduledRunDoesNotReschedule()
        {
            var parser = new Program.ConsolePatchParser(new[] { "driver.1337", "driver.dll", "-schedule", "-scheduledrun" });
            int engineCalls = 0;
            var coordinator = new PatchElevationCoordinator(_ =>
            {
                engineCalls++;
                return PatchOutcome.SuccessOutcome("Patched.");
            }, new Privileges(false), new NeverLauncher());
            var result = await Program.ExecuteConsoleRequestAsync(parser, coordinator, new Privileges(false), _ =>
            {
                Assert.Fail("Scheduled replay must not reschedule.");
                return ScheduledPatchResult.Failure("Unexpected scheduling.");
            });
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, engineCalls);
        }

        [TestMethod]
        public async Task UnelevatedWorkerCannotExecutePatch()
        {
            var parser = new Program.ConsolePatchParser(new[] { "driver.1337", "driver.dll", "-elevatedworker" });
            int engineCalls = 0;
            var coordinator = new PatchElevationCoordinator(_ =>
            {
                engineCalls++;
                return PatchOutcome.SuccessOutcome("Unexpected patch.");
            }, new Privileges(false), new NeverLauncher());
            var result = await Program.ExecuteConsoleRequestAsync(parser, coordinator, new Privileges(false), _ =>
                ScheduledPatchResult.Failure("Unused."));
            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, engineCalls);
        }

        [TestMethod]
        public async Task SchedulingOccursWithoutPatchingOrImplicitElevation()
        {
            var parser = new Program.ConsolePatchParser(new[] { "driver.1337", "driver.dll", "-schedule", "-elevate", "-backup" });
            var coordinator = new PatchElevationCoordinator(_ =>
            {
                Assert.Fail("Scheduling must not apply the patch.");
                return null;
            }, new Privileges(false), new NeverLauncher());
            PatchScheduleDescriptor scheduled = null;
            var result = await Program.ExecuteConsoleRequestAsync(parser, coordinator, new Privileges(false), descriptor =>
            {
                scheduled = descriptor;
                return ScheduledPatchResult.SuccessResult("Scheduled.", "test", "test");
            });
            Assert.IsTrue(result.Success);
            Assert.IsTrue(scheduled.Elevate);
            Assert.IsTrue(scheduled.CreateBackup);
            Assert.IsFalse(scheduled.TakeOwnership);
        }

        [TestMethod]
        public async Task ElevatedWorkerExecutesFreshValidation()
        {
            var parser = new Program.ConsolePatchParser(new[] { "driver.1337", "driver.dll", "-elevatedworker" });
            var result = await Program.ExecuteConsoleRequestAsync(parser,
                new PatchElevationCoordinator(request => PatchOutcome.Failure("Expected bytes changed."),
                    new Privileges(true), new NeverLauncher()),
                new Privileges(true), _ => ScheduledPatchResult.Failure("Unused."));
            Assert.IsFalse(result.Success);
            Assert.AreEqual("Expected bytes changed.", result.Message);
        }

        private sealed class Privileges : IPrivilegeContext
        {
            public Privileges(bool elevated) { IsElevated = elevated; }
            public bool IsElevated { get; }
        }

        private sealed class NeverLauncher : IElevatedPatchLauncher
        {
            public Task<PatchOutcome> LaunchAsync(PatchRequest request)
            {
                Assert.Fail("This route must not elevate.");
                return Task.FromResult(PatchOutcome.Failure("Unexpected elevation."));
            }
        }
    }

    [TestClass]
    public sealed class ScheduledPatchManagerTests
    {
        [TestMethod]
        public void ScheduledCommandCarriesOnlyExplicitElevationAndOwnership()
        {
            var descriptor = new PatchScheduleDescriptor(@"C:\patch folder\a&b.1337", @"C:\target.dll",
                false, true, false, elevate: true);
            Assert.AreEqual("\"C:\\patcher.exe\" -patch \"C:\\patch folder\\a&b.1337\" \"C:\\target.dll\" -backup -elevate -scheduledrun",
                ScheduledPatchManager.BuildScheduledCommandLine(descriptor, @"C:\patcher.exe"));
            Assert.AreEqual("\"C:\\patcher.exe\" -patch \"C:\\a.1337\" \"C:\\b.dll\" -scheduledrun",
                ScheduledPatchManager.BuildScheduledCommandLine(new PatchScheduleDescriptor(@"C:\a.1337", @"C:\b.dll", false, false, false),
                    @"C:\patcher.exe"));
        }

        [TestMethod]
        public void BuildsCommandLineWithExpectedTokens()
        {
            var descriptor = new PatchScheduleDescriptor("C:\\patches\\driver.1337", "C:\\Program Files\\driver.dll", fixOffset: true, createBackup: true, takeOwnership: true);
            var commandLine = ScheduledPatchManager.BuildScheduledCommandLine(descriptor);

            var expectedPrefix = "\"" + Application.ExecutablePath + "\" -patch ";
            StringAssert.StartsWith(commandLine, expectedPrefix,
                "The scheduled command must start with the quoted native executable path followed by ' -patch '.");
            StringAssert.Contains(commandLine, "-fileoffset");
            StringAssert.Contains(commandLine, "-backup");
            StringAssert.Contains(commandLine, "-takeownership");
            StringAssert.Contains(commandLine, "-scheduledrun");
            StringAssert.Contains(commandLine, "\"C:\\patches\\driver.1337\"");
            StringAssert.Contains(commandLine, "\"C:\\Program Files\\driver.dll\"");
        }

        [TestMethod]
        public void BuildsCommandLineQuotesExecutablePathWithSpaces()
        {
            var descriptor = new PatchScheduleDescriptor("C:\\patches\\driver.1337", "C:\\Program Files\\driver.dll", fixOffset: true, createBackup: true, takeOwnership: true);
            var commandLine = ScheduledPatchManager.BuildScheduledCommandLine(descriptor, "C:\\dotnet tools\\Win_1337_Patch.exe");

            StringAssert.StartsWith(commandLine, "\"C:\\dotnet tools\\Win_1337_Patch.exe\" -patch ",
                "An executable path containing spaces must stay fully quoted before the ' -patch ' separator.");
            StringAssert.StartsWith(commandLine,
                "\"C:\\dotnet tools\\Win_1337_Patch.exe\" -patch \"C:\\patches\\driver.1337\" \"C:\\Program Files\\driver.dll\"",
                "The command must launch the exact supplied executable with the patch and target arguments in order.");
            Assert.AreEqual(
                "\"C:\\dotnet tools\\Win_1337_Patch.exe\" -patch \"C:\\patches\\driver.1337\" \"C:\\Program Files\\driver.dll\" -fileoffset -backup -takeownership -scheduledrun",
                commandLine,
                "The scheduled command must be exactly the quoted supplied executable plus the original switches in order.");
            StringAssert.Contains(commandLine, "\"C:\\patches\\driver.1337\"");
            StringAssert.Contains(commandLine, "\"C:\\Program Files\\driver.dll\"");
            StringAssert.Contains(commandLine, "-fileoffset");
            StringAssert.Contains(commandLine, "-backup");
            StringAssert.Contains(commandLine, "-takeownership");
            StringAssert.Contains(commandLine, "-scheduledrun");
        }
    }
}
