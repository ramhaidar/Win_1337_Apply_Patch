using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

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

    internal enum PatchFailureKind { None, Validation, AccessDenied, IoFailure, PostMutationFailure, PrivilegedOperationFailed }
    internal enum PatchStage { None, PatchRead, TargetRead, TargetWrite, Backup, Ownership, Normalize }

    public sealed class PatchOutcome
    {
        private PatchOutcome(bool success, string message, string backupPath, Exception error)
        {
            Success = success;
            Message = message;
            BackupPath = backupPath;
            Error = error;
        }

        public bool Success { get; }
        public string Message { get; }
        public string BackupPath { get; }
        public Exception Error { get; }
        internal PatchFailureKind FailureKind { get; private set; }
        internal PatchStage Stage { get; private set; }
        internal string FailurePath { get; private set; }
        internal bool TargetMayBeModified { get; private set; }
        internal bool CanRetryElevated { get; private set; }

        public static PatchOutcome SuccessOutcome(string message, string backupPath = null)
        {
            return new PatchOutcome(true, message, backupPath, null);
        }

        public static PatchOutcome Failure(string message, Exception error = null)
        {
            return Failed(message, PatchFailureKind.Validation, PatchStage.None, null, error);
        }

        internal static PatchOutcome Failed(string message, PatchFailureKind kind, PatchStage stage,
            string path, Exception error = null, bool targetMayBeModified = false, string backupPath = null)
        {
            return new PatchOutcome(false, message, backupPath, error)
            {
                FailureKind = kind,
                Stage = stage,
                FailurePath = path,
                TargetMayBeModified = targetMayBeModified,
                CanRetryElevated = kind == PatchFailureKind.AccessDenied && !targetMayBeModified &&
                    (stage == PatchStage.TargetRead || stage == PatchStage.TargetWrite || stage == PatchStage.Backup)
            };
        }
    }

    public static class PatchEngine
    {
        private const int FileOffsetAdjustment = 0xC00;

        internal static PatchOutcome ApplyPatch(PatchRequest request, PatchExecutionContext context, Action<string> log = null)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(context);

            var stage = PatchStage.None;
            string failurePath = null;
            string backupPath = null;
            bool mutationStarted = false;

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

                stage = PatchStage.PatchRead;
                failurePath = patchFile;
                var lines = context.Files.ReadPatchLines(patchFile);
                if (lines.Length == 0)
                {
                    return PatchOutcome.Failure("Patch file is empty.");
                }

                var header = lines[0].Trim();
                if (!header.StartsWith('>'))
                    return PatchOutcome.Failure("Patch file does not start with a valid header.");

                var expectedName = header.Substring(1).Trim();
                if (string.IsNullOrWhiteSpace(expectedName))
                    return PatchOutcome.Failure("Patch header does not contain a target filename.");

                var expectedNameLower = Path.GetFileName(expectedName).ToLowerInvariant();
                var actualNameLower = Path.GetFileName(targetFile).ToLowerInvariant();
                if (!string.Equals(expectedNameLower, actualNameLower, StringComparison.Ordinal))
                    return PatchOutcome.Failure($"The .1337 file is not valid for '{Path.GetFileName(targetFile)}'. Expected '{Path.GetFileName(expectedName)}'.");

                var entries = new List<PatchEntry>();
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

                    var adjustedOffset = (long)offset - offsetAdjustment;
                    if (adjustedOffset < 0 || adjustedOffset > int.MaxValue)
                        return PatchOutcome.Failure($"Line {i + 1}: computed offset 0x{adjustedOffset:X} is outside the target file.");

                    if (!byte.TryParse(tokens[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var expectedByte))
                        return PatchOutcome.Failure($"Line {i + 1}: expected byte '{tokens[0]}' is not valid hexadecimal.");

                    if (!byte.TryParse(tokens[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var replacementByte))
                        return PatchOutcome.Failure($"Line {i + 1}: replacement byte '{tokens[1]}' is not valid hexadecimal.");

                    entries.Add(new PatchEntry((int)adjustedOffset, expectedByte, replacementByte, i + 1));
                }

                stage = PatchStage.TargetRead;
                failurePath = targetFile;
                using (var original = context.Files.OpenTarget(targetFile, FileAccess.Read))
                {
                    var validation = ValidateAndReplace(ReadBuffer(original), entries);
                    if (validation != null)
                        return validation;
                }

                stage = PatchStage.TargetWrite;
                Stream writableTarget;
                try
                {
                    writableTarget = context.Files.OpenTarget(targetFile, FileAccess.ReadWrite);
                }
                catch (UnauthorizedAccessException) when (request.TakeOwnership && context.Privileges.IsElevated)
                {
                    stage = PatchStage.Ownership;
                    var ownership = context.Ownership.Grant(targetFile, log);
                    if (!ownership.Success)
                        return PatchOutcome.Failed(ownership.Message, PatchFailureKind.PrivilegedOperationFailed,
                            PatchStage.Ownership, targetFile, ownership.Error);
                    stage = PatchStage.TargetWrite;
                    writableTarget = context.Files.OpenTarget(targetFile, FileAccess.ReadWrite);
                }
                using (var target = writableTarget)
                {
                    var buffer = ReadBuffer(target);
                    var validation = ValidateAndReplace(buffer, entries);
                    if (validation != null)
                        return validation;

                    if (request.CreateBackup)
                    {
                        stage = PatchStage.Backup;
                        backupPath = context.Files.CreateBackup(targetFile, target, log);
                    }

                    stage = PatchStage.TargetWrite;
                    target.Position = 0;
                    mutationStarted = true;
                    target.Write(buffer, 0, buffer.Length);
                    target.Flush();
                    log?.Invoke($"Wrote {buffer.Length} bytes to '{targetFile}'.");
                }

                if (!request.SkipChecksum)
                {
                    stage = PatchStage.Normalize;
                    context.Files.Normalize(targetFile, log);
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
                var kind = mutationStarted ? PatchFailureKind.PostMutationFailure :
                    ex is UnauthorizedAccessException ? PatchFailureKind.AccessDenied : PatchFailureKind.IoFailure;
                var message = $"Patch failed during {stage}: {ex.Message}";
                if (mutationStarted)
                    message += " The target may have changed; do not retry without inspecting or restoring it.";
                if (backupPath != null)
                    message += $" Backup saved to {backupPath}.";
                return PatchOutcome.Failed(message, kind, stage, failurePath, ex, mutationStarted, backupPath);
            }
        }

        public static PatchOutcome ApplyPatch(PatchRequest request, Action<string> log = null)
        {
            return ApplyPatch(request, new PatchExecutionContext(new PatchFileOperations(),
                new WindowsPrivilegeContext(), new FileOwnershipService()), log);
        }

        private static byte[] ReadBuffer(Stream stream)
        {
            stream.Position = 0;
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        private static PatchOutcome ValidateAndReplace(byte[] buffer, List<PatchEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.Offset >= buffer.Length)
                    return PatchOutcome.Failure($"Line {entry.Line}: computed offset 0x{entry.Offset:X} is outside the target file.");
                if (buffer[entry.Offset] != entry.Expected)
                    return PatchOutcome.Failure($"Offset 0x{entry.Offset:X} mismatch: found 0x{buffer[entry.Offset]:X2}, expected 0x{entry.Expected:X2}.");
                buffer[entry.Offset] = entry.Replacement;
            }
            return null;
        }

        private readonly struct PatchEntry
        {
            public PatchEntry(int offset, byte expected, byte replacement, int line)
            {
                Offset = offset;
                Expected = expected;
                Replacement = replacement;
                Line = line;
            }
            public int Offset { get; }
            public byte Expected { get; }
            public byte Replacement { get; }
            public int Line { get; }
        }

    }
}
