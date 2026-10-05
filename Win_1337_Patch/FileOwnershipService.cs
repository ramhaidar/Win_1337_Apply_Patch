using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Win_1337_Patch
{
    internal interface IOwnershipCommandRunner
    {
        int Run(string executablePath, IReadOnlyList<string> arguments, Action<string> log);
    }

    internal sealed class FileOwnershipService : IFileOwnershipService
    {
        private readonly IOwnershipCommandRunner runner;
        private readonly string systemDirectory;

        public FileOwnershipService() : this(new OwnershipCommandRunner(),
            Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative")
                : Environment.SystemDirectory)
        { }

        public FileOwnershipService(IOwnershipCommandRunner runner, string systemDirectory)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            this.systemDirectory = systemDirectory ?? throw new ArgumentNullException(nameof(systemDirectory));
        }

        internal static IReadOnlyList<string> BuildTakeownArguments(string targetPath) => new[] { "/F", targetPath };
        internal static IReadOnlyList<string> BuildIcaclsArguments(string targetPath) =>
            new[] { targetPath, "/grant", "*S-1-5-32-544:F" };

        public PatchOutcome Grant(string targetPath, Action<string> log = null)
        {
            bool ownershipAttempted = false;
            try
            {
                ownershipAttempted = true;
                var takeownCode = runner.Run(Path.Combine(systemDirectory, "takeown.exe"), BuildTakeownArguments(targetPath), log);
                if (takeownCode != 0)
                    return Failure($"takeown failed (exit {takeownCode}); ownership may have changed. Patching stopped.", targetPath);
                var aclCode = runner.Run(Path.Combine(systemDirectory, "icacls.exe"), BuildIcaclsArguments(targetPath), log);
                if (aclCode != 0)
                    return Failure($"icacls failed (exit {aclCode}); ownership may have changed but access was not granted. Patching stopped.", targetPath);
                log?.Invoke($"Ownership and administrator access updated for '{targetPath}'.");
                return PatchOutcome.SuccessOutcome("Ownership fallback completed.");
            }
            catch (Exception ex)
            {
                var warning = ownershipAttempted ? " Ownership may have changed." : string.Empty;
                return Failure($"Ownership fallback failed: {ex.Message}.{warning} Patching stopped.", targetPath, ex);
            }
        }

        private static PatchOutcome Failure(string message, string path, Exception error = null) =>
            PatchOutcome.Failed(message, PatchFailureKind.PrivilegedOperationFailed, PatchStage.Ownership, path, error);
    }

    internal sealed class OwnershipCommandRunner : IOwnershipCommandRunner
    {
        public int Run(string executablePath, IReadOnlyList<string> arguments, Action<string> log)
        {
            var info = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
                info.ArgumentList.Add(argument);
            using (var process = Process.Start(info) ?? throw new IOException("Could not start ownership utility."))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                var output = stdout.GetAwaiter().GetResult();
                var error = stderr.GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(output))
                    log?.Invoke(output.Trim());
                if (!string.IsNullOrWhiteSpace(error))
                    log?.Invoke(error.Trim());
                return process.ExitCode;
            }
        }
    }
}
