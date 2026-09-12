using System;
using System.Configuration;
using System.IO;
using System.Xml;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class PortableSettingsProviderTests
    {
        private string directory;
        private string settingsPath;

        private sealed class TestProvider : PortableSettingsProvider
        {
            public string DirectoryPath;
            public override string[] GetAppSettingsPaths() { return new[] { DirectoryPath }; }
            public override string GetAppSettingsFileName() { return "SuperPuTTY.settings"; }
        }

        private sealed class InterruptedDocument : XmlDocument
        {
            public override void Save(Stream stream)
            {
                stream.WriteByte((byte)'<');
                throw new IOException("Simulated interrupted write");
            }
        }

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-AtomicSettings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            settingsPath = Path.Combine(directory, "SuperPuTTY.settings");
        }

        [TearDown]
        public void TearDown() { Directory.Delete(directory, true); }

        private static XmlDocument Document(string value)
        {
            var document = new XmlDocument();
            document.LoadXml("<Settings><UpdateChannel>" + value + "</UpdateChannel></Settings>");
            return document;
        }

        private string ReadChannel(string path)
        {
            var document = new XmlDocument();
            document.Load(path);
            return document.SelectSingleNode("/Settings/UpdateChannel").InnerText;
        }

        [Test]
        public void FirstSaveCreatesReadablePreferencesWithoutTemporaryFiles()
        {
            PortableSettingsProvider.SaveAtomically(Document(UpdateChannel.CommunityFork), settingsPath);
            Assert.AreEqual(UpdateChannel.CommunityFork, ReadChannel(settingsPath));
            Assert.False(File.Exists(settingsPath + ".bak"));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Test]
        public void ReplacementRetainsExactlyThePreviousSaveAsBackup()
        {
            File.WriteAllText(settingsPath, Document("first").OuterXml);
            byte[] first = File.ReadAllBytes(settingsPath);
            PortableSettingsProvider.SaveAtomically(Document("second"), settingsPath);
            CollectionAssert.AreEqual(first, File.ReadAllBytes(settingsPath + ".bak"));
            byte[] second = File.ReadAllBytes(settingsPath);
            PortableSettingsProvider.SaveAtomically(Document("third"), settingsPath);
            CollectionAssert.AreEqual(second, File.ReadAllBytes(settingsPath + ".bak"));
            Assert.AreEqual("third", ReadChannel(settingsPath));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Test]
        public void InterruptedSerializationPreservesOriginalAndBackup()
        {
            File.WriteAllText(settingsPath, Document("original").OuterXml);
            File.WriteAllText(settingsPath + ".bak", Document("backup").OuterXml);
            byte[] original = File.ReadAllBytes(settingsPath);
            byte[] backup = File.ReadAllBytes(settingsPath + ".bak");
            Assert.Throws<IOException>(() => PortableSettingsProvider.SaveAtomically(new InterruptedDocument(), settingsPath));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(settingsPath));
            CollectionAssert.AreEqual(backup, File.ReadAllBytes(settingsPath + ".bak"));
            Assert.AreEqual("original", ReadChannel(settingsPath));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Test]
        public void FailedReplacementPreservesOriginalAndBackup()
        {
            File.WriteAllText(settingsPath, Document("original").OuterXml);
            File.WriteAllText(settingsPath + ".bak", Document("backup").OuterXml);
            byte[] original = File.ReadAllBytes(settingsPath);
            byte[] backup = File.ReadAllBytes(settingsPath + ".bak");
            // Allow readers, but deny the delete sharing required to replace the file.
            using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Throws<IOException>(() => PortableSettingsProvider.SaveAtomically(Document("new"), settingsPath));
            }
            CollectionAssert.AreEqual(original, File.ReadAllBytes(settingsPath));
            CollectionAssert.AreEqual(backup, File.ReadAllBytes(settingsPath + ".bak"));
            Assert.AreEqual("original", ReadChannel(settingsPath));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        }

        [TestCase(null, UpdateChannel.CommunityFork)]
        [TestCase(UpdateChannel.OfficialUpstream, UpdateChannel.OfficialUpstream)]
        [TestCase(UpdateChannel.CommunityFork, UpdateChannel.CommunityFork)]
        public void NewProfilesDefaultToCommunityAndStoredChoicesArePreserved(string stored, string expected)
        {
            if (stored != null) File.WriteAllText(settingsPath, Document(stored).OuterXml);
            var provider = new TestProvider { DirectoryPath = directory };
            // Use the production default metadata, without reading the developer's profile.
            var metadata = new SuperPutty.Properties.Settings().Properties["UpdateChannel"];
            var property = new SettingsProperty(metadata) { Provider = provider };
            var properties = new SettingsPropertyCollection { property };
            var values = provider.GetPropertyValues(new SettingsContext(), properties);
            Assert.AreEqual(expected, values["UpdateChannel"].PropertyValue);
            provider.SetPropertyValues(new SettingsContext(), values);
            Assert.AreEqual(expected, ReadChannel(settingsPath));
            if (stored != null) Assert.AreEqual(stored, ReadChannel(settingsPath + ".bak"));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        }
    }
}
