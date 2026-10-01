using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Win_1337_Patch.Tests
{
    /// <summary>
    /// Task 2 migration validation: settings metadata and the app.config fixture
    /// must stay on the modern System.Configuration.ConfigurationManager stack.
    /// These tests only inspect metadata and a copied fixture; they never call
    /// Save, Reset or Upgrade and never touch the current user's saved settings.
    /// </summary>
    [TestClass]
    public sealed class RuntimeCompatibilityTests
    {
        private static string GetFixturePath()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "MigrationFixtures", "app.config");
            Assert.IsTrue(File.Exists(path), $"Fixture copy of app.config missing at '{path}'.");
            return path;
        }

        [TestMethod]
        public void SettingsDefaultsRequireExplicitOwnership()
        {
            var settings = Win_1337_Patch.Properties.Settings.Default;

            var names = settings.Properties.Cast<SettingsProperty>()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(new[] { "backup", "changeOwnership", "fixoffset" }, names,
                "Settings must define exactly fixoffset, backup and changeOwnership.");

            foreach (SettingsProperty property in settings.Properties)
            {
                Assert.AreEqual(typeof(bool), property.PropertyType, $"Setting '{property.Name}' must remain bool.");
                Assert.IsNotNull(property.Attributes[typeof(UserScopedSettingAttribute)],
                    $"Setting '{property.Name}' must remain user-scoped.");
                Assert.IsInstanceOfType(property.DefaultValue, typeof(string),
                    $"Setting '{property.Name}' default must be the serialized attribute value.");
                Assert.AreEqual(property.Name == "changeOwnership" ? "False" : "True", (string)property.DefaultValue,
                    $"Setting '{property.Name}' must use the safe default.");
            }
        }

        [TestMethod]
        public void AppConfigurationUsesModernSettingsSections()
        {
            var fixturePath = GetFixturePath();
            var raw = File.ReadAllText(fixturePath);

            Assert.IsFalse(raw.Contains("supportedRuntime"),
                "app.config must not keep the Framework supportedRuntime element.");
            Assert.IsFalse(raw.Contains("System, Version=4.0.0.0"),
                "app.config section declarations must not target Framework System v4.0.0.0.");

            var map = new ExeConfigurationFileMap { ExeConfigFilename = fixturePath };
            var config = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None);

            Assert.IsNull(XDocument.Load(fixturePath).Root?.Element("startup"),
                "app.config must not keep a <startup> element (config.Sections['startup'] is non-null even for inherited IgnoreSection defaults).");

            var userSettingsGroup = Assert.IsInstanceOfType<UserSettingsGroup>(config.SectionGroups["userSettings"],
                "userSettings must deserialize as a UserSettingsGroup.");
            StringAssert.StartsWith(userSettingsGroup.Type, "System.Configuration.UserSettingsGroup, System.Configuration.ConfigurationManager",
                "userSettings group must bind to the modern System.Configuration.ConfigurationManager assembly.");

            var clientSection = Assert.IsInstanceOfType<ClientSettingsSection>(userSettingsGroup.Sections["Win_1337_Patch.Properties.Settings"],
                "Win_1337_Patch.Properties.Settings must deserialize as a ClientSettingsSection.");
            StringAssert.StartsWith(clientSection.SectionInformation.Type, "System.Configuration.ClientSettingsSection, System.Configuration.ConfigurationManager",
                "ClientSettingsSection must bind to the modern System.Configuration.ConfigurationManager assembly.");
            Assert.IsFalse(clientSection.SectionInformation.Type.Contains("Version=4.0.0.0"),
                "ClientSettingsSection must not reference the Framework System v4.0.0.0 assembly.");

            var elements = clientSection.Settings.Cast<SettingElement>()
                .OrderBy(element => element.Name, StringComparer.Ordinal)
                .ToArray();

            Assert.AreEqual(3, elements.Length, "Exactly three user settings must be declared.");
            CollectionAssert.AreEqual(new[] { "backup", "changeOwnership", "fixoffset" }, elements.Select(element => element.Name).ToArray());

            foreach (var element in elements)
            {
                Assert.IsNotNull(element.Value.ValueXml, $"Setting '{element.Name}' must carry a serialized value node.");
                Assert.AreEqual(element.Name == "changeOwnership" ? "False" : "True", element.Value.ValueXml.InnerText,
                    $"Setting '{element.Name}' must serialize its safe default.");
            }
        }

        [STATestMethod]
        public void FormLoadsOriginalResourcesAndLayout()
        {
            var previousContext = SynchronizationContext.Current;
            Form1 form = null;
            try
            {
                form = new Form1();

                Assert.IsNotNull(form.Icon, "Form1 must keep the embedded vampire icon from 1337.resx.");
                StringAssert.Contains(form.Text, "v2.3",
                    "Form1 title must keep the original v2.3 version text.");
                Assert.AreEqual(AutoScaleMode.Font, form.AutoScaleMode,
                    "Form1 must keep the original designer AutoScaleMode.Font.");
                Assert.AreEqual(6F, form.AutoScaleDimensions.Width,
                    "Form1 must keep the original designer AutoScaleDimensions width (6F).");
                Assert.AreEqual(13F, form.AutoScaleDimensions.Height,
                    "Form1 must keep the original designer AutoScaleDimensions height (13F).");
                Assert.AreEqual("Microsoft Sans Serif", form.Font.Name,
                    "Form1 must keep the original Microsoft Sans Serif form font on modern .NET.");
                Assert.AreEqual(8.25F, form.Font.SizeInPoints,
                    "Form1 must keep the original 8.25pt form font on modern .NET.");
            }
            finally
            {
                form?.Dispose();
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        [TestMethod]
        public void NormalStartupDoesNotRequestElevation()
        {
            var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MigrationFixtures", "app.manifest"));
            var executionLevel = document.Descendants(XName.Get("requestedExecutionLevel", "urn:schemas-microsoft-com:asm.v3")).Single();
            Assert.AreEqual("asInvoker", executionLevel.Attribute("level").Value);
            Assert.AreEqual("false", executionLevel.Attribute("uiAccess").Value);
        }

        [TestMethod]
        public void SettingsSourceHasSameSafeDefaultsAsRuntime()
        {
            var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MigrationFixtures", "Settings.settings"));
            XNamespace ns = "http://schemas.microsoft.com/VisualStudio/2004/01/settings";
            foreach (var setting in document.Descendants(ns + "Setting"))
            {
                var name = setting.Attribute("Name").Value;
                Assert.AreEqual(name == "changeOwnership" ? "False" : "True", setting.Element(ns + "Value").Value);
            }
        }

        [STATestMethod]
        public void FormIgnoresSavedOwnershipAndDoesNotSaveOnLoad()
        {
            var previousContext = SynchronizationContext.Current;
            var settings = new FakeSettings { FixOffset = false, CreateBackup = false };
            try
            {
                using (var form = CreateTestForm(settings))
                {
                    form.InitializePreferences();
                    Assert.IsFalse(((CheckBox)form.Controls["cchangeOwnership"]).Checked);
                    Assert.IsFalse(((CheckBox)form.Controls["cfileoffsett"]).Checked);
                    Assert.IsFalse(((CheckBox)form.Controls["controlloBackup"]).Checked);
                    Assert.AreEqual(0, settings.Saves);
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        [STATestMethod]
        public void GuiOwnershipCancellationDoesNotStartPatch()
        {
            var testContext = SynchronizationContext.Current;
            int engineCalls = 0;
            var coordinator = new PatchElevationCoordinator(_ =>
            {
                engineCalls++;
                return PatchOutcome.SuccessOutcome("Patched.");
            }, new FakePrivileges(), new NeverLauncher());
            using (var form = new Form1(coordinator, new FakeSettings(), _ => Task.FromResult(false), _ => false))
            {
                ((CheckBox)form.Controls["cchangeOwnership"]).Checked = true;
                var outcome = CompleteWithUiPump(form.ExecuteGuiPatchAsync("patch.1337", "target.dll"));
                Assert.IsFalse(outcome.Success);
                Assert.AreEqual(0, engineCalls);
                Assert.IsFalse(((CheckBox)form.Controls["cchangeOwnership"]).Checked);
            }
            SynchronizationContext.SetSynchronizationContext(testContext);
        }

        [STATestMethod]
        public void GuiForwardsOnlyCurrentOperationOwnershipConsent()
        {
            var testContext = SynchronizationContext.Current;
            bool requestedOwnership = false;
            var coordinator = new PatchElevationCoordinator(request =>
            {
                requestedOwnership = request.TakeOwnership;
                return PatchOutcome.SuccessOutcome("Patched.");
            }, new FakePrivileges(), new NeverLauncher());
            using (var form = new Form1(coordinator, new FakeSettings(), _ => Task.FromResult(false), _ => true))
            {
                ((CheckBox)form.Controls["cchangeOwnership"]).Checked = true;
                var outcome = CompleteWithUiPump(form.ExecuteGuiPatchAsync("patch.1337", "target.dll"));
                Assert.IsTrue(outcome.Success);
                Assert.IsTrue(requestedOwnership);
                Assert.IsFalse(((CheckBox)form.Controls["cchangeOwnership"]).Checked);
                CompleteWithUiPump(form.ExecuteGuiPatchAsync("patch.1337", "target.dll"));
                Assert.IsFalse(requestedOwnership);
            }
            SynchronizationContext.SetSynchronizationContext(testContext);
        }

        private static PatchOutcome CompleteWithUiPump(Task<PatchOutcome> operation)
        {
            var timeout = Stopwatch.StartNew();
            while (!operation.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(5))
                Application.DoEvents();
            Assert.IsTrue(operation.IsCompleted, "The GUI operation must complete while the UI message queue is processed.");
            return operation.GetAwaiter().GetResult();
        }

        private static Form1 CreateTestForm(FakeSettings settings)
        {
            var coordinator = new PatchElevationCoordinator(_ => PatchOutcome.SuccessOutcome("Unused."),
                new FakePrivileges(), new NeverLauncher());
            return new Form1(coordinator, settings, _ => Task.FromResult(false), _ => false);
        }

        private sealed class FakeSettings : IGuiPatchSettings
        {
            public bool FixOffset { get; set; } = true;
            public bool CreateBackup { get; set; } = true;
            public bool SavedOwnership => true;
            public int Saves { get; private set; }
            public void Save() { Saves++; }
        }

        private sealed class FakePrivileges : IPrivilegeContext
        {
            public bool IsElevated => false;
        }

        private sealed class NeverLauncher : IElevatedPatchLauncher
        {
            public Task<PatchOutcome> LaunchAsync(PatchRequest request)
            {
                Assert.Fail("This GUI test must not launch an elevated child.");
                return Task.FromResult(PatchOutcome.Failure("Unexpected launch."));
            }
        }
    }
}
