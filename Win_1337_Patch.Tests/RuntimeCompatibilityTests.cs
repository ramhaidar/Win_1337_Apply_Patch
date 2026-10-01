using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
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
        public void SettingsDefaultsRemainTrue()
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
                Assert.AreEqual("True", (string)property.DefaultValue, $"Setting '{property.Name}' must default to True.");
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
                Assert.AreEqual("True", element.Value.ValueXml.InnerText, $"Setting '{element.Name}' must serialize as True.");
            }
        }

        [STATestMethod]
        public void FormLoadsOriginalResourcesAndLayout()
        {
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
            }
        }
    }
}
