using Microsoft.VisualStudio.TestTools.UnitTesting;
using Win_1337_Patch;

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
    }

    [TestClass]
    public sealed class ScheduledPatchManagerTests
    {
        [TestMethod]
        public void BuildsCommandLineWithExpectedTokens()
        {
            var descriptor = new PatchScheduleDescriptor("C:\\patches\\driver.1337", "C:\\Program Files\\driver.dll", fixOffset: true, createBackup: true, takeOwnership: true);
            var commandLine = ScheduledPatchManager.BuildScheduledCommandLine(descriptor);

            StringAssert.Contains(commandLine, "-fileoffset");
            StringAssert.Contains(commandLine, "-backup");
            StringAssert.Contains(commandLine, "-takeownership");
            StringAssert.Contains(commandLine, "-scheduledrun");
            StringAssert.Contains(commandLine, "\"C:\\patches\\driver.1337\"");
            StringAssert.Contains(commandLine, "\"C:\\Program Files\\driver.dll\"");
        }
    }
}
