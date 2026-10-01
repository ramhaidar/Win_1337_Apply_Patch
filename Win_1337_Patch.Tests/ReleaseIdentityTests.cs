using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Win_1337_Patch.Tests
{
    /// <summary>
    /// Guards the released version identity of the executable.
    ///
    /// A release published as v2.4 whose binary reports an older assembly/file version
    /// makes Windows file properties and any antivirus/vendor submission attribute the
    /// official artifact to the wrong version. Windows Explorer's "File version" comes
    /// from AssemblyFileVersion, so both attributes are asserted here.
    /// </summary>
    [TestClass]
    public sealed class ReleaseIdentityTests
    {
        private static readonly Version ExpectedVersion = new Version(2, 4, 0, 0);

        [TestMethod]
        public void CompiledAssemblyVersionMatchesReleaseIdentity()
        {
            var version = typeof(Form1).Assembly.GetName().Version;

            Assert.AreEqual(ExpectedVersion, version,
                "AssemblyVersion must match the released version; otherwise the official v2.4 binary " +
                "reports an older version to Windows and to AV/vendor submission processes.");
        }

        [TestMethod]
        public void CompiledFileVersionMatchesReleaseIdentity()
        {
            var path = typeof(Form1).Assembly.Location;
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);

            Assert.AreEqual(ExpectedVersion.ToString(), info.FileVersion,
                "AssemblyFileVersion must match the release identity because Windows file properties " +
                "(Explorer 'File version') display this value.");
        }
    }
}
