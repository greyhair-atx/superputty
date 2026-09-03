using System;
using System.IO;
using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class SettingsFolderResolverTests
    {
        private string testRoot;

        [SetUp]
        public void SetUp()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "SuperPutty-SettingsFolder-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }

        [Test]
        public void DefaultFolderUsesLocalAppDataInsteadOfDocuments()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            Assert.AreEqual(
                Path.Combine(localAppData, "SuperPuTTY"),
                SettingsFolderResolver.DefaultSettingsFolder);
            StringAssert.DoesNotStartWith(documents, SettingsFolderResolver.DefaultSettingsFolder);
        }

        [Test]
        public void ExistingLegacyDocumentsFolderIsPreferredForAnUnconfiguredProfile()
        {
            string legacy = Path.Combine(testRoot, "Documents", "SuperPuTTY");
            Directory.CreateDirectory(legacy);

            Assert.AreEqual(
                legacy,
                SettingsFolderResolver.GetConfiguredOrLegacyFolder(String.Empty, legacy));
        }

        [Test]
        public void ExplicitConfigurationTakesPriorityOverLegacyDocumentsFolder()
        {
            string configured = Path.Combine(testRoot, "configured");
            string legacy = Path.Combine(testRoot, "Documents", "SuperPuTTY");
            Directory.CreateDirectory(legacy);

            Assert.AreEqual(
                configured,
                SettingsFolderResolver.GetConfiguredOrLegacyFolder(configured, legacy));
        }

        [Test]
        public void MissingLegacyDocumentsFolderLeavesProfileUnconfigured()
        {
            string legacy = Path.Combine(testRoot, "missing", "SuperPuTTY");

            Assert.Null(SettingsFolderResolver.GetConfiguredOrLegacyFolder(String.Empty, legacy));
        }

        [Test]
        public void EmptyConfigurationUsesWritableFallback()
        {
            string fallback = Path.Combine(testRoot, "local-app-data");
            bool usedFallback;
            Exception configuredError;

            string result = SettingsFolderResolver.ResolveWritableFolder(
                String.Empty,
                fallback,
                out usedFallback,
                out configuredError);

            Assert.AreEqual(fallback, result);
            Assert.False(usedFallback);
            Assert.Null(configuredError);
            Assert.True(Directory.Exists(fallback));
        }

        [Test]
        public void WritableConfiguredFolderIsPreserved()
        {
            string configured = Path.Combine(testRoot, "configured");
            string fallback = Path.Combine(testRoot, "fallback");
            bool usedFallback;
            Exception configuredError;

            string result = SettingsFolderResolver.ResolveWritableFolder(
                configured,
                fallback,
                out usedFallback,
                out configuredError);

            Assert.AreEqual(configured, result);
            Assert.False(usedFallback);
            Assert.Null(configuredError);
            Assert.False(Directory.Exists(fallback));
        }

        [Test]
        public void UnwritableConfiguredPathUsesFallback()
        {
            string configured = Path.Combine(testRoot, "not-a-directory");
            File.WriteAllText(configured, "occupied");
            string fallback = Path.Combine(testRoot, "fallback");
            bool usedFallback;
            Exception configuredError;

            string result = SettingsFolderResolver.ResolveWritableFolder(
                configured,
                fallback,
                out usedFallback,
                out configuredError);

            Assert.AreEqual(fallback, result);
            Assert.True(usedFallback);
            Assert.NotNull(configuredError);
            Assert.True(Directory.Exists(fallback));
        }

        [Test]
        public void ExistingSessionsAndLayoutsAreCopiedWithoutOverwritingFallback()
        {
            string source = Path.Combine(testRoot, "source");
            string destination = Path.Combine(testRoot, "destination");
            Directory.CreateDirectory(Path.Combine(source, "layouts"));
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(source, "Sessions.XML"), "source sessions");
            File.WriteAllText(Path.Combine(source, "AutoRestoreLayout.XML"), "source auto restore");
            File.WriteAllText(Path.Combine(source, "layouts", "work.xml"), "source layout");
            File.WriteAllText(Path.Combine(destination, "Sessions.XML"), "existing sessions");

            SettingsFolderResolver.CopyExistingSettings(source, destination);

            Assert.AreEqual("existing sessions", File.ReadAllText(Path.Combine(destination, "Sessions.XML")));
            Assert.AreEqual("source auto restore", File.ReadAllText(Path.Combine(destination, "AutoRestoreLayout.XML")));
            Assert.AreEqual("source layout", File.ReadAllText(Path.Combine(destination, "layouts", "work.xml")));
        }
    }
}
