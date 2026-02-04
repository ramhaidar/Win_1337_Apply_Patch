using System;
using System.IO;

namespace Win_1337_Patch
{
    internal static class PatchTargetResolver
    {
        private const string NvEncodeApiPatchName = "nvencodeapi.1337";
        private const string NvEncodeApi64PatchName = "nvencodeapi64.1337";
        private const string NvEncodeApiFileName = "nvEncodeAPI.dll";
        private const string NvEncodeApi64FileName = "nvEncodeAPI64.dll";

        internal static bool TryGetAutoTarget(string patchFilePath, out string targetPath)
        {
            targetPath = null;

            if (string.IsNullOrWhiteSpace(patchFilePath))
                return false;

            string patchFileName = Path.GetFileName(patchFilePath);
            if (string.IsNullOrWhiteSpace(patchFileName))
                return false;

            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(windowsDirectory))
                return false;

            if (string.Equals(patchFileName, NvEncodeApiPatchName, StringComparison.OrdinalIgnoreCase))
            {
                targetPath = Path.Combine(windowsDirectory, "SysWOW64", NvEncodeApiFileName);
                return true;
            }

            if (string.Equals(patchFileName, NvEncodeApi64PatchName, StringComparison.OrdinalIgnoreCase))
            {
                targetPath = Path.Combine(windowsDirectory, "System32", NvEncodeApi64FileName);
                return true;
            }

            return false;
        }
    }
}
