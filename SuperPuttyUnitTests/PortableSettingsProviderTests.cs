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

        private static SettingsPropertyCollection ChannelProperties(PortableSettingsProvider provider)
        {
            var metadata = new SuperPutty.Properties.Settings().Properties["UpdateChannel"];
            return new SettingsPropertyCollection { new SettingsProperty(metadata) { Provider = provider } };
        }

        private static SettingsPropertyValueCollection ChannelValues(PortableSettingsProvider provider, string value)
        {
            return new SettingsPropertyValueCollection
            {
                new SettingsPropertyValue(ChannelProperties(provider)["UpdateChannel"]) { PropertyValue = value }
            };
        }

        [TestCase("<Settings><UpdateChannel>")]
        [TestCase("<NotSettings/>")]
        [TestCase("<Settings xmlns='urn:unexpected'/>")]
        [TestCase("<!DOCTYPE Settings [<!ENTITY value 'unexpected'>]><Settings>&value;</Settings>")]
        public void InvalidPrimaryRecoversValidatedBackupAndPreservesOriginal(string damaged)
        {
            File.WriteAllText(settingsPath, damaged);
            File.WriteAllText(settingsPath + ".bak", Document(UpdateChannel.OfficialUpstream).OuterXml);
            byte[] original = File.ReadAllBytes(settingsPath);
            byte[] backup = File.ReadAllBytes(settingsPath + ".bak");
            var provider = new TestProvider { DirectoryPath = directory };

            var values = provider.GetPropertyValues(new SettingsContext(), ChannelProperties(provider));

            Assert.AreEqual(UpdateChannel.OfficialUpstream, values["UpdateChannel"].PropertyValue);
            Assert.IsNotNull(provider.RecoveryNotification);
            Assert.AreEqual(settingsPath, provider.RecoveryNotification.SettingsFilePath);
            Assert.AreEqual(settingsPath + ".bak", provider.RecoveryNotification.BackupFilePath);
            StringAssert.StartsWith(settingsPath + ".corrupt.", provider.RecoveryNotification.PreservedFilePath);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(provider.RecoveryNotification.PreservedFilePath));
            CollectionAssert.AreEqual(backup, File.ReadAllBytes(settingsPath));
            CollectionAssert.AreEqual(backup, File.ReadAllBytes(settingsPath + ".bak"));

            values["UpdateChannel"].PropertyValue = UpdateChannel.CommunityFork;
            provider.SetPropertyValues(new SettingsContext(), values);
            CollectionAssert.AreEqual(backup, File.ReadAllBytes(settingsPath + ".bak"));
            Assert.AreEqual(UpdateChannel.CommunityFork, ReadChannel(settingsPath));
            values["UpdateChannel"].PropertyValue = UpdateChannel.OfficialUpstream;
            provider.SetPropertyValues(new SettingsContext(), values);
            Assert.AreEqual(UpdateChannel.CommunityFork, ReadChannel(settingsPath + ".bak"));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(provider.RecoveryNotification.PreservedFilePath));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Test]
        public void MissingPrimaryRecoversBackupWithoutCreatingDamagedFile()
        {
            File.WriteAllText(settingsPath + ".bak", Document(UpdateChannel.OfficialUpstream).OuterXml);
            byte[] backup = File.ReadAllBytes(settingsPath + ".bak");
            var provider = new TestProvider { DirectoryPath = directory };

            var values = provider.GetPropertyValues(new SettingsContext(), ChannelProperties(provider));

            Assert.AreEqual(UpdateChannel.OfficialUpstream, values["UpdateChannel"].PropertyValue);
            Assert.IsNotNull(provider.RecoveryNotification);
            Assert.IsNull(provider.RecoveryNotification.PreservedFilePath);
            CollectionAssert.AreEqual(backup, File.ReadAllBytes(settingsPath));
            CollectionAssert.AreEqual(backup, File.ReadAllBytes(settingsPath + ".bak"));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.corrupt.*"));
        }

        [TestCase(null)]
        [TestCase("<Settings>")]
        [TestCase("<NotSettings/>")]
        public void InvalidPrimaryWithoutValidBackupNeverFallsBackToOrSavesDefaults(string backupText)
        {
            File.WriteAllText(settingsPath, "<Settings><UpdateChannel>");
            if (backupText != null) File.WriteAllText(settingsPath + ".bak", backupText);
            byte[] original = File.ReadAllBytes(settingsPath);
            var provider = new TestProvider { DirectoryPath = directory };

            Assert.Throws<ConfigurationErrorsException>(() => provider.GetPropertyValues(new SettingsContext(), ChannelProperties(provider)));
            // A second access and an attempted save must not use a partially cached document.
            Assert.Throws<ConfigurationErrorsException>(() => { var document = provider.SettingsXML; });
            Assert.Throws<ConfigurationErrorsException>(() => provider.SetPropertyValues(new SettingsContext(),
                ChannelValues(provider, UpdateChannel.CommunityFork)));

            CollectionAssert.AreEqual(original, File.ReadAllBytes(settingsPath));
            if (backupText != null) Assert.AreEqual(backupText, File.ReadAllText(settingsPath + ".bak"));
            else Assert.False(File.Exists(settingsPath + ".bak"));
            Assert.IsNull(provider.RecoveryNotification);
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.corrupt.*"));
        }

        [Test]
        public void UnreadablePrimaryDoesNotUseBackupOrCacheDefaults()
        {
            File.WriteAllText(settingsPath, Document(UpdateChannel.CommunityFork).OuterXml);
            File.WriteAllText(settingsPath + ".bak", Document(UpdateChannel.OfficialUpstream).OuterXml);
            var provider = new TestProvider { DirectoryPath = directory };
            using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<ConfigurationErrorsException>(() => provider.GetPropertyValues(new SettingsContext(), ChannelProperties(provider)));
                Assert.Throws<ConfigurationErrorsException>(() => provider.SetPropertyValues(new SettingsContext(),
                    ChannelValues(provider, UpdateChannel.OfficialUpstream)));
                Assert.IsNull(provider.RecoveryNotification);
            }

            var values = provider.GetPropertyValues(new SettingsContext(), ChannelProperties(provider));
            Assert.AreEqual(UpdateChannel.CommunityFork, values["UpdateChannel"].PropertyValue);
            Assert.AreEqual(UpdateChannel.CommunityFork, ReadChannel(settingsPath));
            Assert.AreEqual(UpdateChannel.OfficialUpstream, ReadChannel(settingsPath + ".bak"));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.corrupt.*"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnreadableBackupDoesNotReplaceMissingOrInvalidPrimary(bool primaryExists)
        {
            const string damaged = "<Settings>";
            if (primaryExists) File.WriteAllText(settingsPath, damaged);
            File.WriteAllText(settingsPath + ".bak", Document(UpdateChannel.OfficialUpstream).OuterXml);
            var provider = new TestProvider { DirectoryPath = directory };

            using (var locked = new FileStream(settingsPath + ".bak", FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<ConfigurationErrorsException>(() => provider.GetPropertyValues(new SettingsContext(), ChannelProperties(provider)));
                Assert.Throws<ConfigurationErrorsException>(() => provider.SetPropertyValues(new SettingsContext(),
                    ChannelValues(provider, UpdateChannel.CommunityFork)));
                Assert.IsNull(provider.RecoveryNotification);
                Assert.AreEqual(primaryExists, File.Exists(settingsPath));
                if (primaryExists) Assert.AreEqual(damaged, File.ReadAllText(settingsPath));
            }

            Assert.AreEqual(UpdateChannel.OfficialUpstream, ReadChannel(settingsPath + ".bak"));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.corrupt.*"));
        }

        [Test]
        public void FailedRecoveryReplacementPreservesBothFilesAndCanBeRetried()
        {
            const string damaged = "<Settings><UpdateChannel>";
            File.WriteAllText(settingsPath, damaged);
            File.WriteAllText(settingsPath + ".bak", Document(UpdateChannel.OfficialUpstream).OuterXml);
            byte[] backup = File.ReadAllBytes(settingsPath + ".bak");
            var provider = new TestProvider { DirectoryPath = directory };
            using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Throws<ConfigurationErrorsException>(() => provider.GetPropertyValues(new SettingsContext(), ChannelProperties(provider)));
                Assert.IsNull(provider.RecoveryNotification);
                Assert.AreEqual(damaged, File.ReadAllText(settingsPath));
                CollectionAssert.AreEqual(backup, File.ReadAllBytes(settingsPath + ".bak"));
                Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
                Assert.IsEmpty(Directory.GetFiles(directory, "*.corrupt.*"));
            }

            var values = provider.GetPropertyValues(new SettingsContext(), ChannelProperties(provider));
            Assert.AreEqual(UpdateChannel.OfficialUpstream, values["UpdateChannel"].PropertyValue);
            Assert.AreEqual(damaged, File.ReadAllText(provider.RecoveryNotification.PreservedFilePath));
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

        [TestCase(null)]
        [TestCase("False")]
        [TestCase("True")]
        public void MiddleClickPreferenceDefaultsOffAndRoundTrips(string stored)
        {
            const string name = "MiddleClickClosesWithoutConfirmation";
            if (stored != null) File.WriteAllText(settingsPath, "<Settings><" + name + ">" + stored + "</" + name + "></Settings>");
            var provider = new TestProvider { DirectoryPath = directory };
            var metadata = new SuperPutty.Properties.Settings().Properties[name];
            var properties = new SettingsPropertyCollection { new SettingsProperty(metadata) { Provider = provider } };
            var values = provider.GetPropertyValues(new SettingsContext(), properties);
            Assert.AreEqual(stored == "True", values[name].PropertyValue);
            values[name].PropertyValue = !(bool)values[name].PropertyValue;
            provider.SetPropertyValues(new SettingsContext(), values);
            var reloaded = new TestProvider { DirectoryPath = directory }.GetPropertyValues(new SettingsContext(), properties);
            Assert.AreEqual(stored != "True", reloaded[name].PropertyValue);
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
