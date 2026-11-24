using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SuperPutty.Data;
using App = SuperPutty.SuperPuTTY;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable]
    public class SessionCatalogStartupTests
    {
        private string directory;
        private string originalFolder;
        private App.PreparedCatalog originalCatalog;
        private string SessionsFile { get { return Path.Combine(directory, "Sessions.XML"); } }

        [SetUp]
        public void SetUp()
        {
            originalFolder = App.Settings.SettingsFolder;
            originalCatalog = App.CaptureCatalog();
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-StartupCatalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            App.Settings.SettingsFolder = directory;
            App.ReplaceSessionCatalog(new SessionData[0]);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                App.AcceptCatalog(originalCatalog, true);
            }
            finally
            {
                App.Settings.SettingsFolder = originalFolder;
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void InvalidInitialCatalogCannotBeOverwrittenBySavingAnEmptyCatalog()
        {
            File.WriteAllText(SessionsFile, "<ArrayOfSessionData><SessionData");
            byte[] original = File.ReadAllBytes(SessionsFile);

            App.LoadInitialSessions();

            Assert.False(App.IsSessionCatalogReady);
            Assert.Throws<InvalidOperationException>(App.SaveSessions);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(SessionsFile));
            Assert.AreEqual(1, Directory.GetFileSystemEntries(directory).Length);

            File.WriteAllText(SessionsFile, "<ArrayOfSessionData/>");
            App.LoadSessions();
            Assert.True(App.IsSessionCatalogReady);
        }

        [Test]
        public void UnreadableInitialCatalogIsNotTreatedAsAMissingFile()
        {
            // A directory is reported as nonexistent by File.Exists, but cannot be
            // opened as a session file. It must not grant permission to save defaults.
            Directory.CreateDirectory(SessionsFile);

            App.LoadInitialSessions();

            Assert.False(App.IsSessionCatalogReady);
            Assert.Throws<InvalidOperationException>(App.SaveSessions);
            Assert.True(Directory.Exists(SessionsFile));
        }

        [Test]
        public void MissingInitialCatalogAllowsFirstSave()
        {
            App.LoadInitialSessions();

            Assert.True(App.IsSessionCatalogReady);
            Assert.DoesNotThrow(App.SaveSessions);
            Assert.True(File.Exists(SessionsFile));
        }

        [Test]
        public void FailedLaterReloadPreservesReadinessAndTheLastValidCatalog()
        {
            File.WriteAllText(SessionsFile,
                "<ArrayOfSessionData><SessionData SessionId='Original' SessionName='Original'/></ArrayOfSessionData>");
            App.LoadInitialSessions();
            var original = App.GetSessionById("Original");
            Assert.IsNotNull(original);

            using (var locked = new FileStream(SessionsFile, FileMode.Open, FileAccess.Read, FileShare.None))
                App.LoadSessions();

            Assert.True(App.IsSessionCatalogReady);
            Assert.AreSame(original, App.GetSessionById("Original"));
        }
    }
}
