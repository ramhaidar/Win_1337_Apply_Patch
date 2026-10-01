using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Forms;
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
