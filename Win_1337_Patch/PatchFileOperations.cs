using System;
using System.IO;
using System.Security.Principal;
using System.Runtime.InteropServices;

namespace Win_1337_Patch
{
    internal interface IPatchFileOperations
    {
        string[] ReadPatchLines(string path);
        Stream OpenTarget(string path, FileAccess access);
        string CreateBackup(string targetPath, Stream original, Action<string> log);
        void Normalize(string path, Action<string> log);
    }

    internal interface IPrivilegeContext
    {
        bool IsElevated { get; }
    }

    internal interface IFileOwnershipService
    {
        PatchOutcome Grant(string targetPath, Action<string> log = null);
    }

    internal sealed class PatchExecutionContext
    {
        public PatchExecutionContext(IPatchFileOperations files, IPrivilegeContext privileges, IFileOwnershipService ownership)
        {
            Files = files ?? throw new ArgumentNullException(nameof(files));
            Privileges = privileges ?? throw new ArgumentNullException(nameof(privileges));
            Ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        }

        public IPatchFileOperations Files { get; }
        public IPrivilegeContext Privileges { get; }
        public IFileOwnershipService Ownership { get; }
    }

    internal sealed class WindowsPrivilegeContext : IPrivilegeContext
    {
        public bool IsElevated
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }

    internal sealed class PatchFileOperations : IPatchFileOperations
    {
        public string[] ReadPatchLines(string path) => File.ReadAllLines(path);

        public Stream OpenTarget(string path, FileAccess access)
        {
            return new FileStream(path, FileMode.Open, access, FileShare.None);
        }

        public string CreateBackup(string targetPath, Stream original, Action<string> log)
        {
            var path = $"{targetPath}.{DateTime.Now:yyyy-MM-dd_hh-mm-ss-tt}.{Guid.NewGuid():N}.BAK";
            var position = original.Position;
            try
            {
                original.Position = 0;
                using (var backup = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    original.CopyTo(backup);
            }
            finally
            {
                original.Position = position;
            }
            log?.Invoke($"Backup created at '{path}'.");
            return path;
        }

        public void Normalize(string path, Action<string> log)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                ImageRemoveCertificate(stream.SafeFileHandle.DangerousGetHandle(), 0);
            checked
            {
                if (!new mCheckSum().FixCheckSum(path))
                    throw new IOException("Checksum recalculation failed.");
            }
            log?.Invoke("PE checksum normalized.");
        }

        [DllImport("Imagehlp.dll")]
        private static extern bool ImageRemoveCertificate(IntPtr handle, int index);
    }
}
