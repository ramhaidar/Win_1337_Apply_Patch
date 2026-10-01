using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Win_1337_Patch.Tests
{
    [TestClass]
    public sealed class PatchElevationCoordinatorTests
    {
        [TestMethod]
        public async Task DeniedPatchNeedsExplicitElevation()
        {
            var launcher = new Launcher();
            var coordinator = new PatchElevationCoordinator(_ => Denied(), new Privileges(false), launcher);
            var result = await coordinator.ApplyAsync(Request(), false, false);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, launcher.Calls);
        }

        [TestMethod]
        public async Task ExplicitElevationLaunchesOneOperation()
        {
            var launcher = new Launcher();
            var coordinator = new PatchElevationCoordinator(_ => Denied(), new Privileges(false), launcher);
            var result = await coordinator.ApplyAsync(Request(), true, false);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, launcher.Calls);
        }

        [TestMethod]
        public async Task ConfirmationCancellationDoesNotLaunchChild()
        {
            var launcher = new Launcher();
            var coordinator = new PatchElevationCoordinator(_ => Denied(), new Privileges(false), launcher);
            var result = await coordinator.ApplyAsync(Request(), true, false, _ => Task.FromResult(false));
            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, launcher.Calls);
        }

        [TestMethod]
        [DataRow(true, false)]
        [DataRow(false, true)]
        public async Task ElevatedOrWorkerInvocationDoesNotRelaunch(bool elevated, bool worker)
        {
            var launcher = new Launcher();
            var result = await new PatchElevationCoordinator(_ => Denied(), new Privileges(elevated), launcher)
                .ApplyAsync(Request(), true, worker);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, launcher.Calls);
        }

        [TestMethod]
        public async Task WritablePatchDoesNotLaunchChild()
        {
            var launcher = new Launcher();
            var result = await new PatchElevationCoordinator(_ => PatchOutcome.SuccessOutcome("Patched."), new Privileges(false), launcher)
                .ApplyAsync(Request(), true, false);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, launcher.Calls);
        }

        [TestMethod]
        public async Task PostMutationFailureDoesNotLaunchChild()
        {
            var launcher = new Launcher();
            var failure = PatchOutcome.Failed("Changed target.", PatchFailureKind.PostMutationFailure,
                PatchStage.Normalize, "target.dll", targetMayBeModified: true);
            var result = await new PatchElevationCoordinator(_ => failure, new Privileges(false), launcher)
                .ApplyAsync(Request(), true, false);
            Assert.IsTrue(result.TargetMayBeModified);
            Assert.AreEqual(0, launcher.Calls);
        }

        [TestMethod]
        public async Task ChildFailureDoesNotBecomeSuccess()
        {
            var launcher = new Launcher { Result = PatchOutcome.Failure("Worker failed.") };
            var result = await new PatchElevationCoordinator(_ => Denied(), new Privileges(false), launcher)
                .ApplyAsync(Request(), true, false);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, launcher.Calls);
        }

        private static PatchRequest Request() => new PatchRequest("patch.1337", "target.dll");
        private static PatchOutcome Denied() => PatchOutcome.Failed("Denied.", PatchFailureKind.AccessDenied,
            PatchStage.TargetWrite, "target.dll", new UnauthorizedAccessException());

        private sealed class Privileges : IPrivilegeContext
        {
            public Privileges(bool elevated) { IsElevated = elevated; }
            public bool IsElevated { get; }
        }

        private sealed class Launcher : IElevatedPatchLauncher
        {
            public int Calls { get; private set; }
            public PatchOutcome Result { get; set; } = PatchOutcome.SuccessOutcome("Child succeeded.");
            public Task<PatchOutcome> LaunchAsync(PatchRequest request)
            {
                Calls++;
                return Task.FromResult(Result);
            }
        }
    }
}
