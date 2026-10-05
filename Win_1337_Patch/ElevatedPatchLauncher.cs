using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Win_1337_Patch
{
    internal interface IElevatedProcessRunner
    {
        Task<int> RunAsync(ProcessStartInfo startInfo);
    }

    internal sealed class ElevatedPatchLauncher : IElevatedPatchLauncher
    {
        private readonly IElevatedProcessRunner runner;
        private readonly string executablePath;

        public ElevatedPatchLauncher() : this(new ElevatedProcessRunner(), Environment.ProcessPath) { }

        public ElevatedPatchLauncher(IElevatedProcessRunner runner, string executablePath)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            this.executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
        }

        internal static string BuildArguments(PatchRequest request)
        {
            var arguments = new StringBuilder("-patch ");
            arguments.Append(QuoteWindowsArgument(Path.GetFullPath(request.PatchFilePath)));
            arguments.Append(' ').Append(QuoteWindowsArgument(Path.GetFullPath(request.TargetFilePath)));
            if (request.FixFileOffset)
                arguments.Append(" -fileoffset");
            if (request.CreateBackup)
                arguments.Append(" -backup");
            if (request.TakeOwnership)
                arguments.Append(" -takeownership");
            arguments.Append(" -elevatedworker");
            return arguments.ToString();
        }

        public static string QuoteWindowsArgument(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var quoted = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                quoted.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
                quoted.Append(character);
                backslashes = 0;
            }
            quoted.Append('\\', backslashes * 2).Append('"');
            return quoted.ToString();
        }

        public async Task<PatchOutcome> LaunchAsync(PatchRequest request)
        {
            try
            {
                var info = new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = BuildArguments(request)
                };
                var exitCode = await runner.RunAsync(info);
                return exitCode == 0
                    ? PatchOutcome.SuccessOutcome("Elevated patch operation completed successfully. See the child console for details and backup location.")
                    : PatchOutcome.Failure($"Elevated patch operation failed (exit {exitCode}). See the child console for details; inspect the target before retrying.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return PatchOutcome.Failure("Administrator operation cancelled by UAC. The elevated patch was not started.", ex);
            }
            catch (Exception ex)
            {
                return PatchOutcome.Failure($"Could not complete the elevated operation: {ex.Message}. Inspect the target before retrying if a child process started.", ex);
            }
        }
    }

    internal sealed class ElevatedProcessRunner : IElevatedProcessRunner
    {
        public async Task<int> RunAsync(ProcessStartInfo startInfo)
        {
            using (var process = Process.Start(startInfo) ?? throw new IOException("Could not start the elevated process."))
            {
                await process.WaitForExitAsync();
                return process.ExitCode;
            }
        }
    }
}
