using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Win_1337_Patch
{
    public sealed class PatchRequest
    {
        public PatchRequest(string patchFilePath, string targetFilePath, bool fixFileOffset = false, bool createBackup = false, bool takeOwnership = false)
        {
            PatchFilePath = patchFilePath ?? throw new ArgumentNullException(nameof(patchFilePath));
            TargetFilePath = targetFilePath ?? throw new ArgumentNullException(nameof(targetFilePath));
            FixFileOffset = fixFileOffset;
            CreateBackup = createBackup;
            TakeOwnership = takeOwnership;
            SkipChecksum = false;
        }

        internal PatchRequest(string patchFilePath, string targetFilePath, bool fixFileOffset, bool createBackup, bool takeOwnership, bool skipChecksum)
        {
            PatchFilePath = patchFilePath ?? throw new ArgumentNullException(nameof(patchFilePath));
            TargetFilePath = targetFilePath ?? throw new ArgumentNullException(nameof(targetFilePath));
            FixFileOffset = fixFileOffset;
            CreateBackup = createBackup;
            TakeOwnership = takeOwnership;
            SkipChecksum = skipChecksum;
        }

        public string PatchFilePath { get; }
        public string TargetFilePath { get; }
        public bool FixFileOffset { get; }
        public bool CreateBackup { get; }
        public bool TakeOwnership { get; }
        internal bool SkipChecksum { get; }
    }

    public sealed class PatchOutcome
    {
        private PatchOutcome(bool success, string message, string? backupPath, Exception? error)
        {
            Success = success;
            Message = message;
            BackupPath = backupPath;
            Error = error;
        }

        public bool Success { get; }
        public string Message { get; }
        public string? BackupPath { get; }
        public Exception? Error { get; }

        public static PatchOutcome SuccessOutcome(string message, string? backupPath = null)
        {
            return new PatchOutcome(true, message, backupPath, null);
        }

        public static PatchOutcome Failure(string message, Exception? error = null)
        {
            return new PatchOutcome(false, message, null, error);
        }
    }

    public static class PatchEngine
    {
        private const int FileOffsetAdjustment = 0xC00;

        public static PatchOutcome ApplyPatch(PatchRequest request, Action<string>? log = null)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            try
            {
                log?.Invoke("Starting patch engine...");

                var patchFile = Path.GetFullPath(request.PatchFilePath);
                var targetFile = Path.GetFullPath(request.TargetFilePath);

                log?.Invoke($"Patch definition: {patchFile}");
                log?.Invoke($"Target file: {targetFile}");

                if (!File.Exists(patchFile))
                    return PatchOutcome.Failure($"Patch file not found: {patchFile}");

                if (!File.Exists(targetFile))
                    return PatchOutcome.Failure($"Target file not found: {targetFile}");

                if (request.TakeOwnership)
                {
                    var ownership = GrantOwnership(targetFile, log);
                    if (!ownership.Success)
                        return PatchOutcome.Failure(ownership.Message, ownership.Error);
                }

                var lines = File.ReadAllLines(patchFile);
                if (lines.Length == 0)
                {
                    return PatchOutcome.Failure("Patch file is empty.");
                }

                var header = lines[0].Trim();
                if (!header.StartsWith(">", StringComparison.Ordinal))
                    return PatchOutcome.Failure("Patch file does not start with a valid header.");

                var expectedName = header.Substring(1).Trim();
                if (string.IsNullOrWhiteSpace(expectedName))
                    return PatchOutcome.Failure("Patch header does not contain a target filename.");

                var expectedNameLower = Path.GetFileName(expectedName).ToLowerInvariant();
                var actualNameLower = Path.GetFileName(targetFile).ToLowerInvariant();
                if (!string.Equals(expectedNameLower, actualNameLower, StringComparison.Ordinal))
                    return PatchOutcome.Failure($"The .1337 file is not valid for '{Path.GetFileName(targetFile)}'. Expected '{Path.GetFileName(expectedName)}'.");

                var buffer = File.ReadAllBytes(targetFile);
                var offsetAdjustment = request.FixFileOffset ? FileOffsetAdjustment : 0;

                for (var i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;

                    var colonIndex = line.IndexOf(':');
                    if (colonIndex <= 0 || colonIndex == line.Length - 1)
                        return PatchOutcome.Failure($"Line {i + 1}: invalid patch entry.");

                    var offsetText = line.Substring(0, colonIndex).Trim();
                    var remainder = line.Substring(colonIndex + 1);
                    var tokens = remainder.Replace("->", ":").Split(':');

                    if (tokens.Length < 2)
                        return PatchOutcome.Failure($"Line {i + 1}: missing replacement byte.");

                    if (!int.TryParse(offsetText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var offset))
                        return PatchOutcome.Failure($"Line {i + 1}: offset '{offsetText}' is not valid hexadecimal.");

                    var adjustedOffset = offset - offsetAdjustment;
                    if (adjustedOffset < 0 || adjustedOffset >= buffer.Length)
                        return PatchOutcome.Failure($"Line {i + 1}: computed offset 0x{adjustedOffset:X} is outside the target file.");

                    if (!byte.TryParse(tokens[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var expectedByte))
                        return PatchOutcome.Failure($"Line {i + 1}: expected byte '{tokens[0]}' is not valid hexadecimal.");

                    if (!byte.TryParse(tokens[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var replacementByte))
                        return PatchOutcome.Failure($"Line {i + 1}: replacement byte '{tokens[1]}' is not valid hexadecimal.");

                    if (buffer[adjustedOffset] != expectedByte)
                        return PatchOutcome.Failure($"Offset 0x{adjustedOffset:X} mismatch: found 0x{buffer[adjustedOffset]:X2}, expected 0x{expectedByte:X2}.");

                    buffer[adjustedOffset] = replacementByte;
                }

                string? backupPath = null;
                if (request.CreateBackup)
                {
                    backupPath = CreateBackup(targetFile, log);
                    if (backupPath == null)
                        return PatchOutcome.Failure("Unable to create a backup copy of the target file.");
                }

                File.WriteAllBytes(targetFile, buffer);
                log?.Invoke($"Wrote {buffer.Length} bytes to '{targetFile}'.");

                if (!request.SkipChecksum)
                {
                    var normalizeResult = NormalizeFile(targetFile, log);
                    if (!normalizeResult.Success)
                        return PatchOutcome.Failure(normalizeResult.Message, normalizeResult.Error);
                }
                else
                {
                    log?.Invoke("Checksum normalization skipped.");
                }

                var successMessage = $"File {Path.GetFileName(targetFile)} patched successfully.";
                if (!string.IsNullOrEmpty(backupPath))
                    successMessage += $" Backup saved to {backupPath}.";

                return PatchOutcome.SuccessOutcome(successMessage, backupPath);
            }
            catch (Exception ex)
            {
                return PatchOutcome.Failure($"Unexpected error while applying patch: {ex.Message}", ex);
            }
        }

        private static string? CreateBackup(string targetFile, Action<string>? log)
        {
            try
            {
                var backupFileName = $"{targetFile}.{DateTime.Now:yyyy-MM-dd_hh-mm-ss-tt}.BAK";
                if (File.Exists(backupFileName))
                    File.Delete(backupFileName);

                File.Copy(targetFile, backupFileName);
                log?.Invoke($"Backup created at '{backupFileName}'.");
                return backupFileName;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Backup failed: {ex.Message}");
                return null;
            }
        }

        private static FileNormalizationResult NormalizeFile(string filePath, Action<string>? log)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    ImageRemoveCertificate(fs.SafeFileHandle.DangerousGetHandle(), 0);
                }

                checked
                {
                    var checksum = new mCheckSum();
                    if (!checksum.FixCheckSum(filePath))
                        return FileNormalizationResult.Failure("Checksum recalculation failed.");
                }

                log?.Invoke("PE checksum normalized.");
                return FileNormalizationResult.Success("PE checksum normalized.");
            }
            catch (OverflowException ex)
            {
                var message = $"Overflow while normalizing checksum: {ex.Message}";
                log?.Invoke(message);
                return FileNormalizationResult.Failure(message, ex);
            }
            catch (Exception ex)
            {
                var message = $"Failed to normalize checksum: {ex.Message}";
                log?.Invoke(message);
                return FileNormalizationResult.Failure(message, ex);
            }
        }

        private static FileOwnershipResult GrantOwnership(string filePath, Action<string>? log)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = psi })
                {
                    process.Start();
                    using (var writer = process.StandardInput)
                    {
                        if (writer.BaseStream.CanWrite)
                        {
                            writer.WriteLine($"takeown /F \"{filePath}\"");
                            writer.WriteLine($"icacls \"{filePath}\" /grant Administrators:F");
                        }
                    }

                    process.WaitForExit();
                }

                var logMessage = $"Ownership updated for '{filePath}'.";
                log?.Invoke(logMessage);
                return FileOwnershipResult.Success(logMessage);
            }
            catch (Exception ex)
            {
                var message = $"Failed to update ownership: {ex.Message}";
                log?.Invoke(message);
                return FileOwnershipResult.Failure(message, ex);
            }
        }

        [DllImport("Imagehlp.dll")]
        private static extern bool ImageRemoveCertificate(IntPtr handle, int index);

        private readonly struct FileNormalizationResult
        {
            public FileNormalizationResult(bool success, string message, Exception? error)
            {
                Success = success;
                Message = message;
                Error = error;
            }

            public bool Success { get; }
            public string Message { get; }
            public Exception? Error { get; }

            public static FileNormalizationResult Success(string message)
            {
                return new FileNormalizationResult(true, message, null);
            }

            public static FileNormalizationResult Failure(string message, Exception? error = null)
            {
                return new FileNormalizationResult(false, message, error);
            }
        }

        private readonly struct FileOwnershipResult
        {
            public FileOwnershipResult(bool success, string message, Exception? error)
            {
                Success = success;
                Message = message;
                Error = error;
            }

            public bool Success { get; }
            public string Message { get; }
            public Exception? Error { get; }

            public static FileOwnershipResult Success(string message)
            {
                return new FileOwnershipResult(true, message, null);
            }

            public static FileOwnershipResult Failure(string message, Exception? error)
            {
                return new FileOwnershipResult(false, message, error);
            }
        }
    }
}
