using System;
using System.Configuration;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class BrandingCompatibilityTests
    {
        // Under NUnit the entry assembly is the test host. Use the application's
        // unchanged product metadata, just as Application.ProductName does in the EXE.
        private sealed class ProfileProvider : PortableSettingsProvider
        {
            public string[] Paths;
            public override string ApplicationName
            {
                get { return typeof(SuperPutty.SuperPuTTY).Assembly.GetCustomAttribute<AssemblyProductAttribute>().Product; }
                set { }
            }
            public override string[] GetAppSettingsPaths() { return Paths; }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExistingProfileOrPortablePreferencesAreDiscovered(bool portable)
        {
            string root = Path.Combine(Path.GetTempPath(), "SuperPutty-Branding-" + Guid.NewGuid().ToString("N"));
            string profile = Path.Combine(root, "profile");
            string executable = Path.Combine(root, "app");
            Directory.CreateDirectory(profile);
            Directory.CreateDirectory(executable);
            try
            {
                string expected = Path.Combine(portable ? executable : profile, "SuperPuTTY.settings");
                File.WriteAllText(expected, "<Settings><DefaultLayoutName>Operations</DefaultLayoutName></Settings>");
                var provider = new ProfileProvider { Paths = new[] { profile, executable } };
                var properties = new SettingsPropertyCollection();
                properties.Add(new SettingsProperty("DefaultLayoutName")
                {
                    PropertyType = typeof(string), DefaultValue = "", SerializeAs = SettingsSerializeAs.String
                });
                Assert.AreEqual("SuperPuTTY.settings", provider.GetAppSettingsFileName());
                Assert.AreEqual("Operations", provider.GetPropertyValues(new SettingsContext(), properties)["DefaultLayoutName"].PropertyValue);
                Assert.AreEqual(expected, provider.SettingsFilePath);
                Assert.AreEqual(1, Directory.GetFiles(root, "*.settings", SearchOption.AllDirectories).Length);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void LegacySessionXmlStillLoadsWithItsOriginalIdentifiers()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "<ArrayOfSessionData><SessionData SessionId=\"Production/Web\" SessionName=\"Web\" Host=\"server.example.com\" Port=\"22\" Proto=\"SSH\" PuttySession=\"Default Settings\" /></ArrayOfSessionData>");
                var sessions = SessionData.LoadSessionsFromFile(path);
                Assert.AreEqual(1, sessions.Count);
                Assert.AreEqual("Production/Web", sessions[0].SessionId);
                Assert.AreEqual("server.example.com", sessions[0].Host);
                Assert.AreEqual("Default Settings", sessions[0].PuttySession);
                Assert.AreEqual("AutoRestoreLayout.XML", LayoutData.AutoRestoreLayoutFileName);
            }
            finally { File.Delete(path); }
        }
    }
}
