using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Win_1337_Patch
{
    static class Program
    {
        private static bool consoleReady;

        /// <summary>
        /// Punto di ingresso principale dell'applicazione.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (HandleCommandLine(args))
                return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        private static bool HandleCommandLine(string[] args)
        {
            if (args == null || args.Length == 0)
                return false;

            if (args.Any(IsHelpSwitch))
            {
                EnsureConsole();
                ShowUsage();
                return true;
            }

            var patchIndex = Array.FindIndex(args, arg => string.Equals(arg, "-patch", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--patch", StringComparison.OrdinalIgnoreCase));
            if (patchIndex < 0)
                return false;

            RunConsolePatch(args.Skip(patchIndex + 1).ToArray());
            return true;
        }

        private static void RunConsolePatch(string[] args)
        {
            EnsureConsole();
            var parser = new ConsolePatchParser(args);
            if (!parser.IsValid)
            {
                ShowUsage(parser.ErrorMessage);
                Environment.ExitCode = 1;
                return;
            }

            if (parser.RequestHelp)
            {
                ShowUsage();
                return;
            }

            var patchFilePath = parser.PatchFilePath;
            var targetFilePath = parser.TargetFilePath;

            if (string.IsNullOrWhiteSpace(patchFilePath) || string.IsNullOrWhiteSpace(targetFilePath))
            {
                ShowUsage("Internal error: missing patch or target path.");
                Environment.ExitCode = 1;
                return;
            }

            var patchDescription = $"Patch mode: {Path.GetFileName(patchFilePath)} -> {targetFilePath}";

            if (parser.ScheduleOnNextBoot && !parser.ScheduledRun)
            {
                var descriptor = new PatchScheduleDescriptor(patchFilePath, targetFilePath, parser.FixOffset, parser.CreateBackup, parser.TakeOwnership);
                var scheduleResult = ScheduledPatchManager.Schedule(descriptor, LogToConsole);
                Console.WriteLine(scheduleResult.Message);
                Environment.ExitCode = scheduleResult.Success ? 0 : 1;
                return;
            }

            if (parser.ScheduledRun)
            {
                Console.WriteLine();
                Console.WriteLine("Executing previously scheduled patch...");
            }

            Console.WriteLine();
            Console.WriteLine(patchDescription);
            Console.WriteLine("Applying patch...");

            var request = new PatchRequest(patchFilePath, targetFilePath, parser.FixOffset, parser.CreateBackup, parser.TakeOwnership);
            var result = PatchEngine.ApplyPatch(request, LogToConsole);

            Console.WriteLine(result.Message);
            Environment.ExitCode = result.Success ? 0 : 1;
        }

        private static void EnsureConsole()
        {
            if (consoleReady)
                return;

            NativeMethods.AllocConsole();
            Console.OutputEncoding = Encoding.UTF8;
            consoleReady = true;
        }

        private static void ShowUsage(string errorMessage = null)
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
                Console.WriteLine($"Error: {errorMessage}");

            var commandName = Path.GetFileName(Environment.GetCommandLineArgs().FirstOrDefault() ?? "Win_1337_Apply_Patch.exe");
            Console.WriteLine("Usage:");
            Console.WriteLine($"  {commandName} -patch <1337-file> <target-file> [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -fileoffset, --fileoffset   Apply the same 0xC00 offset adjustment used by the GUI.");
            Console.WriteLine("  -backup, --backup           Keep a timestamped backup of the target before patching.");
            Console.WriteLine("  -takeownership              Run takeown/icacls so the patch can overwrite protected files.");
            Console.WriteLine("  -schedule, --schedule, -runonce, --run-once, -run-on-reboot, --run-on-reboot   Schedule the patch to run after the next reboot.");
            Console.WriteLine("  -scheduledrun, --scheduled-run   Internally generated when a scheduled patch executes; you normally do not use this flag directly.");
            Console.WriteLine("  -help, --help, /?           Show this help text.");
            Console.WriteLine();
        }

        private static void LogToConsole(string message)
        {
            Console.WriteLine(message);
        }

        private static bool IsHelpSwitch(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            switch (value.Trim().ToLowerInvariant())
            {
                case "-h":
                case "--help":
                case "/?":
                case "/help":
                    return true;
                default:
                    return false;
            }
        }

        internal sealed class ConsolePatchParser
        {
            private static readonly string[] OffsetSwitches = { "-fileoffset", "--fileoffset", "-offset", "--offset" };
            private static readonly string[] BackupSwitches = { "-backup", "--backup", "-b" };
            private static readonly string[] OwnershipSwitches = { "-takeownership", "--takeownership", "--take-ownership", "-take-ownership" };
            private static readonly string[] ScheduleSwitches = { "-schedule", "--schedule", "-runonce", "--run-once", "-run-on-reboot", "--run-on-reboot" };
            private static readonly string[] ScheduledRunSwitches = { "-scheduledrun", "--scheduled-run" };
            private static readonly string[] PatchSwitches = { "-patch", "--patch" };

            public ConsolePatchParser(string[] args)
            {
                for (int index = 0; index < args.Length; index++)
                {
                    var current = args[index].Trim();
                    if (string.IsNullOrEmpty(current))
                        continue;

                    var normalized = current.ToLowerInvariant();
                    if (IsHelpSwitch(current))
                    {
                        RequestHelp = true;
                        return;
                    }

                    if (PatchSwitches.Contains(normalized))
                        continue;

                    if (OffsetSwitches.Contains(normalized))
                    {
                        FixOffset = true;
                        continue;
                    }

                    if (BackupSwitches.Contains(normalized))
                    {
                        CreateBackup = true;
                        continue;
                    }

                    if (OwnershipSwitches.Contains(normalized))
                    {
                        TakeOwnership = true;
                        continue;
                    }

                    if (ScheduleSwitches.Contains(normalized))
                    {
                        ScheduleOnNextBoot = true;
                        continue;
                    }

                    if (ScheduledRunSwitches.Contains(normalized))
                    {
                        ScheduledRun = true;
                        continue;
                    }

                    if (PatchFilePath == null)
                    {
                        PatchFilePath = current;
                        continue;
                    }

                    if (TargetFilePath == null)
                    {
                        TargetFilePath = current;
                        continue;
                    }

                    ErrorMessage = $"Unknown argument '{current}'.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(PatchFilePath) || string.IsNullOrWhiteSpace(TargetFilePath))
                {
                    ErrorMessage = "Both a .1337 file and a target file are required.";
                    return;
                }

                IsValid = true;
            }

            public bool IsValid { get; private set; }
            public bool RequestHelp { get; private set; }
            public string ErrorMessage { get; private set; }
            public string PatchFilePath { get; private set; }
            public string TargetFilePath { get; private set; }
            public bool FixOffset { get; private set; }
            public bool CreateBackup { get; private set; }
            public bool TakeOwnership { get; private set; }
            public bool ScheduleOnNextBoot { get; private set; }
            public bool ScheduledRun { get; private set; }
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool AllocConsole();
        }
    }
}
