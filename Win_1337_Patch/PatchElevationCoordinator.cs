using System;
using System.Threading.Tasks;

namespace Win_1337_Patch
{
    internal interface IElevatedPatchLauncher
    {
        Task<PatchOutcome> LaunchAsync(PatchRequest request);
    }

    internal sealed class PatchElevationCoordinator
    {
        private readonly Func<PatchRequest, PatchOutcome> apply;
        private readonly IPrivilegeContext privileges;
        private readonly IElevatedPatchLauncher launcher;

        public PatchElevationCoordinator(Func<PatchRequest, PatchOutcome> apply,
            IPrivilegeContext privileges, IElevatedPatchLauncher launcher)
        {
            this.apply = apply ?? throw new ArgumentNullException(nameof(apply));
            this.privileges = privileges ?? throw new ArgumentNullException(nameof(privileges));
            this.launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        }

        internal static PatchElevationCoordinator CreateDefault(Action<string> log = null)
        {
            var privileges = new WindowsPrivilegeContext();
            var context = new PatchExecutionContext(new PatchFileOperations(), privileges, new FileOwnershipService());
            return new PatchElevationCoordinator(request => PatchEngine.ApplyPatch(request, context, log),
                privileges, new ElevatedPatchLauncher());
        }

        public async Task<PatchOutcome> ApplyAsync(PatchRequest request, bool allowElevation, bool elevatedWorker,
            Func<PatchRequest, Task<bool>> confirmElevation = null)
        {
            ArgumentNullException.ThrowIfNull(request);
            var result = await Task.Run(() => apply(request));
            if (result.Success || !result.CanRetryElevated || privileges.IsElevated || elevatedWorker)
                return result;
            if (!allowElevation)
                return PatchOutcome.Failed(result.Message + " Use -elevate to request administrator access, or run from an administrator context.",
                    result.FailureKind, result.Stage, result.FailurePath, result.Error);
            if (confirmElevation != null && !await confirmElevation(request))
                return PatchOutcome.Failure("Administrator operation cancelled. The target was not modified.");
            return await launcher.LaunchAsync(request);
        }
    }
}
