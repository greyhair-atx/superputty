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
        private List<SessionData> originalCatalog;
        private string SessionsFile { get { return Path.Combine(directory, "Sessions.XML"); } }

        private sealed class RestoredStorage : ISessionStorage
        {
            internal List<SessionData> Catalog;
            public Task<List<SessionData>> LoadAsync(string path, CancellationToken token) { return Task.FromResult(Catalog); }
            public void Save(List<SessionData> sessions, string path) { Assert.Fail("Restoring test state must not save."); }
        }

        [SetUp]
        public void SetUp()
        {
            originalFolder = App.Settings.SettingsFolder;
            originalCatalog = App.GetAllSessions();
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-StartupCatalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            App.Settings.SettingsFolder = directory;
            App.ReplaceSessionCatalog(new SessionData[0]);
        }

        [TearDown]
        public void TearDown()
        {
            // A successful load restores readiness as well as the original catalog,
            // without reaching into private state or writing the developer's files.
            if (Directory.Exists(SessionsFile)) Directory.Delete(SessionsFile);
            File.WriteAllText(SessionsFile, "temporary probe file");
            try
            {
                App.LoadSessionsAsync(CancellationToken.None, new RestoredStorage { Catalog = originalCatalog })
                    .GetAwaiter().GetResult();
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
