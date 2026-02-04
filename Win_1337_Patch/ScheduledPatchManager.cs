using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Win_1337_Patch
{
    /// <summary>
    /// Captures the command-line choices that need to be replayed when a patch is scheduled for the next boot.
    /// </summary>
    internal sealed class PatchScheduleDescriptor
    {
        public PatchScheduleDescriptor(string patchFilePath, string targetFilePath, bool fixOffset, bool createBackup, bool takeOwnership)
        {
            PatchFilePath = patchFilePath ?? throw new ArgumentNullException(nameof(patchFilePath));
            TargetFilePath = targetFilePath ?? throw new ArgumentNullException(nameof(targetFilePath));
            FixFileOffset = fixOffset;
            CreateBackup = createBackup;
            TakeOwnership = takeOwnership;
        }

        public string PatchFilePath { get; }
        public string TargetFilePath { get; }
        public bool FixFileOffset { get; }
        public bool CreateBackup { get; }
        public bool TakeOwnership { get; }
    }

    /// <summary>
    /// Result returned when a patch is either scheduled or fails to schedule.
    /// </summary>
    internal sealed class ScheduledPatchResult
    {
        private ScheduledPatchResult(bool success, string message, string? entryName, string? commandLine, Exception? error)
        {
            Success = success;
            Message = message;
            EntryName = entryName;
            CommandLine = commandLine;
            Error = error;
        }

        public bool Success { get; }
        public string Message { get; }
        public string? EntryName { get; }
        public string? CommandLine { get; }
        public Exception? Error { get; }

        public static ScheduledPatchResult SuccessResult(string message, string entryName, string commandLine)
        {
            return new ScheduledPatchResult(true, message, entryName, commandLine, null);
        }

        public static ScheduledPatchResult Failure(string message, Exception? error = null)
        {
            return new ScheduledPatchResult(false, message, null, null, error);
        }
    }

    /// <summary>
    /// Associates a RunOnce registry entry with the patch command so it can execute after the next reboot.
    /// </summary>
    internal static class ScheduledPatchManager
    {
        private const string RunOnceRegistryPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce";

        /// <summary>
        /// Attempts to create a RunOnce value that will execute the patch later.
        /// </summary>
        public static ScheduledPatchResult Schedule(PatchScheduleDescriptor descriptor, Action<string>? log)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));

            var patchPath = Path.GetFullPath(descriptor.PatchFilePath);
            var targetPath = Path.GetFullPath(descriptor.TargetFilePath);

            if (!File.Exists(patchPath))
                return ScheduledPatchResult.Failure($"Patch file not found: {patchPath}");

            if (!File.Exists(targetPath))
                return ScheduledPatchResult.Failure($"Target file not found: {targetPath}");

            var commandLine = BuildScheduledCommandLine(new PatchScheduleDescriptor(patchPath, targetPath, descriptor.FixFileOffset, descriptor.CreateBackup, descriptor.TakeOwnership));

            try
            {
                using (var runOnceKey = Registry.CurrentUser.OpenSubKey(RunOnceRegistryPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunOnceRegistryPath))
                {
                    if (runOnceKey == null)
                        return ScheduledPatchResult.Failure("Could not access the RunOnce registry key.");

                    var entryName = $"Win_1337_Patch_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
                    runOnceKey.SetValue(entryName, commandLine, RegistryValueKind.String);

                    log?.Invoke($"Scheduled run-once entry '{entryName}'.");
                    log?.Invoke($"RunOnce command: {commandLine}");

                    return ScheduledPatchResult.SuccessResult($"Patch scheduled for next boot (entry '{entryName}').", entryName, commandLine);
                }
            }
            catch (Exception ex)
            {
                return ScheduledPatchResult.Failure($"Failed to schedule patch: {ex.Message}", ex);
            }
        }

        internal static string BuildScheduledCommandLine(PatchScheduleDescriptor descriptor)
        {
            var builder = new StringBuilder();
            builder.Append(QuoteArgument(Application.ExecutablePath));
            builder.Append(" -patch ");
            builder.Append(QuoteArgument(descriptor.PatchFilePath));
            builder.Append(" ");
            builder.Append(QuoteArgument(descriptor.TargetFilePath));

            if (descriptor.FixFileOffset)
                builder.Append(" -fileoffset");

            if (descriptor.CreateBackup)
                builder.Append(" -backup");

            if (descriptor.TakeOwnership)
                builder.Append(" -takeownership");

            builder.Append(" -scheduledrun");
            return builder.ToString();
        }

        internal static string QuoteArgument(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            var escaped = value.Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }
    }
}
