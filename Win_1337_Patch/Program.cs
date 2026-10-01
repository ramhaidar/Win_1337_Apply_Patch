using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Win_1337_Patch
{
    static class Program
    {
        private static bool consoleReady;

        internal static async Task<PatchOutcome> ExecuteConsoleRequestAsync(ConsolePatchParser parser,
            PatchElevationCoordinator coordinator, IPrivilegeContext privileges,
            Func<PatchScheduleDescriptor, ScheduledPatchResult> schedule, Action<string> log = null)
        {
            if (!parser.IsValid)
                return PatchOutcome.Failure(parser.ErrorMessage ?? "Invalid patch arguments.");
            if (parser.ElevatedWorker && (!privileges.IsElevated || parser.ScheduleOnNextBoot))
                return PatchOutcome.Failure("An elevated worker requires an administrator token and cannot schedule patches.");
            if (parser.ScheduleOnNextBoot && !parser.ScheduledRun)
            {
                var scheduled = schedule(new PatchScheduleDescriptor(parser.PatchFilePath, parser.TargetFilePath,
                    parser.FixOffset, parser.CreateBackup, parser.TakeOwnership, parser.Elevate));
                return scheduled.Success ? PatchOutcome.SuccessOutcome(scheduled.Message)
                    : PatchOutcome.Failure(scheduled.Message, scheduled.Error);
            }
            var request = new PatchRequest(parser.PatchFilePath, parser.TargetFilePath,
                parser.FixOffset, parser.CreateBackup, parser.TakeOwnership);
            return await coordinator.ApplyAsync(request, parser.Elevate, parser.ElevatedWorker);
        }

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

            if (parser.ScheduledRun)
            {
                Console.WriteLine();
                Console.WriteLine("Executing previously scheduled patch...");
            }

            Console.WriteLine();
            Console.WriteLine(patchDescription);
            Console.WriteLine(parser.ScheduleOnNextBoot && !parser.ScheduledRun ? "Scheduling patch for next login..." : "Applying patch...");

            PatchOutcome result;
            try
            {
                result = ExecuteConsoleRequestAsync(parser, PatchElevationCoordinator.CreateDefault(LogToConsole),
                    new WindowsPrivilegeContext(), descriptor => ScheduledPatchManager.Schedule(descriptor, LogToConsole),
                    LogToConsole).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                result = PatchOutcome.Failure($"Patch operation failed: {ex.Message}", ex);
            }

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
            Console.WriteLine("  -elevate, --elevate         Allow one UAC administrator operation only if normal access fails.");
            Console.WriteLine("  -takeownership              Allow target-only ownership fallback after normal elevated write access fails; also requires -elevate or an administrator context.");
            Console.WriteLine("  -schedule, --schedule, -runonce, --run-once, -run-on-reboot, --run-on-reboot   Schedule for next login; elevation still requires explicit -elevate and UAC consent.");
            Console.WriteLine("  -scheduledrun, --scheduled-run   Internally generated when a scheduled patch executes; you normally do not use this flag directly.");
            Console.WriteLine("  -elevatedworker, --elevated-worker   Internal one-shot worker marker; does not authorize or bypass elevation.");
            Console.WriteLine("  -h, --help, /?             Show this help text.");
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
            private static readonly string[] ElevationSwitches = { "-elevate", "--elevate" };
            private static readonly string[] WorkerSwitches = { "-elevatedworker", "--elevated-worker" };

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

                    if (ElevationSwitches.Contains(normalized))
                    {
                        Elevate = true;
                        continue;
                    }

                    if (WorkerSwitches.Contains(normalized))
                    {
                        ElevatedWorker = true;
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

                if (ElevatedWorker && ScheduleOnNextBoot)
                {
                    ErrorMessage = "An elevated worker cannot schedule patches.";
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
            public bool Elevate { get; private set; }
            public bool ElevatedWorker { get; private set; }
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool AllocConsole();
        }
    }
}
