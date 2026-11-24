using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Utils;
using App = SuperPutty.SuperPuTTY;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable]
    public class SettingsCatalogTransactionTests
    {
        private App.PreparedCatalog original;
        private string originalFolder;
        private string directory;
        private string first;
        private string second;
        private LayoutData originalLayout;

        [SetUp]
        public void SetUp()
        {
            original = App.CaptureCatalog();
            originalFolder = App.Settings.SettingsFolder;
            originalLayout = App.CurrentLayout;
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-SettingsCatalog-" + Guid.NewGuid().ToString("N"));
            first = Path.Combine(directory, "first");
            second = Path.Combine(directory, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            WriteCatalog(first, "First");
            WriteCatalog(second, "Second");
            App.Settings.SettingsFolder = first;
            App.LoadInitialSessions();
            Assert.True(App.IsSessionCatalogReady);
        }

        [TearDown]
        public void TearDown()
        {
            App.Settings.SettingsFolder = originalFolder;
            App.AcceptCatalog(original, true);
            App.LoadLayout(originalLayout, true);
            Directory.Delete(directory, true);
        }

        private static string CatalogPath(string folder) { return Path.Combine(folder, "Sessions.XML"); }

        private static void WriteCatalog(string folder, string id, string host = "original.invalid")
        {
            new SessionStorage().Save(new List<SessionData> {
                new SessionData { SessionId = id, SessionName = id, Host = host }
            }, CatalogPath(folder));
        }

        private Dictionary<string, object> FolderChange()
        {
            return new Dictionary<string, object> { { nameof(SuperPutty.Properties.Settings.SettingsFolder), second } };
        }

        [Test]
        public async Task FirstRunAcceptsTheCatalogEvenWhenTheFolderIsUnchanged()
        {
            var before = App.CaptureCatalog();
            before.Path = null;
            App.AcceptCatalog(before, true);
            Assert.False(App.HasAcceptedCatalog(first));
            Assert.Throws<InvalidOperationException>(App.SaveSessions);

            var prepared = await App.PrepareCatalogAsync(first, CancellationToken.None);
            dlgFindPutty.CommitSettingsChanges(new Dictionary<string, object> {
                { nameof(SuperPutty.Properties.Settings.SettingsFolder), first }
            }, () => { }, prepared);

            Assert.True(App.HasAcceptedCatalog(first));
            Assert.DoesNotThrow(App.SaveSessions);
        }

        [Test]
        public void AFolderChangeCannotBeCommittedWithoutAPreparedCatalog()
        {
            Assert.Throws<InvalidOperationException>(() => dlgFindPutty.CommitSettingsChanges(FolderChange(), () => Assert.Fail()));
            Assert.AreEqual(first, App.Settings.SettingsFolder);
            Assert.IsNotNull(App.GetSessionById("First"));
        }

        [Test]
        public async Task FailedPreferenceSaveRestoresSettingsCatalogAndSaveDestination()
        {
            var firstSession = App.GetSessionById("First");
            var secondBytes = File.ReadAllBytes(CatalogPath(second));
            var prepared = await App.PrepareCatalogAsync(second, CancellationToken.None);
            Assert.AreEqual(first, App.Settings.SettingsFolder);
            Assert.AreSame(firstSession, App.GetSessionById("First"));

            Assert.Throws<IOException>(() => dlgFindPutty.CommitSettingsChanges(FolderChange(),
                () => { throw new IOException("Preference file unavailable"); }, prepared));

            Assert.AreEqual(first, App.Settings.SettingsFolder);
            Assert.AreSame(firstSession, App.GetSessionById("First"));
            Assert.IsNull(App.GetSessionById("Second"));
            Assert.DoesNotThrow(App.SaveSessions);
            CollectionAssert.AreEqual(secondBytes, File.ReadAllBytes(CatalogPath(second)));
        }

        [Test]
        public async Task SuccessfulFolderChangeAcceptsDestinationBeforeSubsequentSaves()
        {
            var firstBytes = File.ReadAllBytes(CatalogPath(first));
            var prepared = await App.PrepareCatalogAsync(second, CancellationToken.None);
            bool persisted = false;
            dlgFindPutty.CommitSettingsChanges(FolderChange(), () => persisted = true, prepared);

            Assert.True(persisted);
            Assert.AreEqual(second, App.Settings.SettingsFolder);
            Assert.IsNull(App.GetSessionById("First"));
            App.GetSessionById("Second").Host = "edited.invalid";
            App.SaveSessions();
            CollectionAssert.AreEqual(firstBytes, File.ReadAllBytes(CatalogPath(first)));
            Assert.AreEqual("edited.invalid", (await new SessionStorage().LoadAsync(CatalogPath(second), CancellationToken.None))[0].Host);
        }

        [Test]
        public void ChangingFolderWithoutAcceptingItsCatalogCannotOverwriteIt()
        {
            var before = File.ReadAllBytes(CatalogPath(second));
            App.Settings.SettingsFolder = second;
            Assert.Throws<InvalidOperationException>(App.SaveSessions);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(CatalogPath(second)));
        }

        [Test]
        public void ExportToTheActiveCatalogRejectsAStaleSnapshot()
        {
            WriteCatalog(first, "First", "other-instance.invalid");
            byte[] before = File.ReadAllBytes(CatalogPath(first));
            string alias = Path.Combine(first, ".", "sessions.xml");
            Assert.ThrowsAsync<InvalidOperationException>(async () => await App.ExportSessionsAsync(alias));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(CatalogPath(first)));
        }

        [Test]
        public async Task ExportToAnotherFolderUsesTheSnapshotCapturedBeforeWriting()
        {
            string destination = CatalogPath(second);
            Task export;
            using (File.Open(destination, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                export = App.ExportSessionsAsync(destination);
                Assert.False(export.IsCompleted);
                App.GetSessionById("First").Host = "edited-after-export.invalid";
            }
            await export;
            var sessions = await new SessionStorage().LoadAsync(destination, CancellationToken.None);
            Assert.AreEqual("original.invalid", sessions[0].Host);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OnlyASuccessfulFolderSwitchDetachesTheLayoutSaveTarget(bool fail)
        {
            var layout = new LayoutData(Path.Combine(first, "layouts", "Work.xml"));
            App.LoadLayout(layout, true);
            var prepared = await App.PrepareCatalogAsync(second, CancellationToken.None);
            if (fail)
            {
                Assert.Throws<IOException>(() => dlgFindPutty.CommitSettingsChanges(FolderChange(),
                    () => { throw new IOException("Preference save rejected"); }, prepared));
                Assert.AreSame(layout, App.CurrentLayout);
            }
            else
            {
                dlgFindPutty.CommitSettingsChanges(FolderChange(), () => { }, prepared);
                Assert.IsNull(App.CurrentLayout);
            }
        }

        [Test]
        public async Task ReloadRequiresPermissionToDiscardMovesAndRetainsThemOnFailure()
        {
            var session = App.GetSessionById("First");
            App.MoveSavedSession(session, "Moved/First");
            Assert.True(App.HasPendingSessionMoves);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await App.LoadSessionsAsync(CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await App.PrepareCatalogAsync(second, CancellationToken.None));

            File.WriteAllText(CatalogPath(first), "<broken>");
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await App.LoadSessionsAsync(CancellationToken.None, discardPendingMoves: true));
            Assert.AreSame(session, App.GetSessionById("Moved/First"));
            Assert.True(App.HasPendingSessionMoves);

            WriteCatalog(first, "First");
            await App.LoadSessionsAsync(CancellationToken.None, discardPendingMoves: true);
            Assert.IsNull(App.GetSessionById("Moved/First"));
            Assert.IsNotNull(App.GetSessionById("First"));
            Assert.False(App.HasPendingSessionMoves);
        }

        [Test]
        public async Task DiscardingMovesWhenSwitchingToAnEmptyFolderDoesNotCarryOverThoseMoves()
        {
            App.MoveSavedSession(App.GetSessionById("First"), "Moved/First");
            File.Delete(CatalogPath(second));
            var prepared = await App.PrepareCatalogAsync(second, CancellationToken.None, discardPendingMoves: true);
            dlgFindPutty.CommitSettingsChanges(FolderChange(), () => { }, prepared);

            Assert.IsNull(App.GetSessionById("Moved/First"));
            Assert.IsNotNull(App.GetSessionById("First"));
            Assert.False(App.HasPendingSessionMoves);
        }

        [Test]
        public async Task PendingMoveSaveWaitsOffThreadAndRejectsAnOverlappingCatalogEdit()
        {
            var session = App.GetSessionById("First");
            App.MoveSavedSession(session, "Moved/First");
            Task save;
            using (File.Open(CatalogPath(first), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                save = App.SaveSessionsAsync();
                Assert.False(save.IsCompleted);
                Assert.True(App.IsSessionWriteInProgress);
                Assert.Throws<InvalidOperationException>(() => App.MoveSavedSession(session, "Conflicting/First"));
            }
            await save;
            Assert.False(App.HasPendingSessionMoves);
            var sessions = await new SessionStorage().LoadAsync(CatalogPath(first), CancellationToken.None);
            Assert.AreEqual("Moved/First", sessions[0].SessionId);
        }

        [Test]
        public void CorruptDestinationCannotChangeCurrentPreferencesOrCatalog()
        {
            File.WriteAllText(CatalogPath(second), "<broken>");
            Assert.ThrowsAsync<InvalidOperationException>(async () => await App.PrepareCatalogAsync(second, CancellationToken.None));
            Assert.AreEqual(first, App.Settings.SettingsFolder);
            Assert.IsNotNull(App.GetSessionById("First"));
            Assert.AreEqual("<broken>", File.ReadAllText(CatalogPath(second)));
        }

        [Test]
        public async Task CatalogEditedDuringPreparationRejectsFolderChangeAndRetainsNewEdits()
        {
            var prepared = await App.PrepareCatalogAsync(second, CancellationToken.None);
            var added = new SessionData { SessionId = "Added", SessionName = "Added" };
            App.AddSession(added);
            bool persisted = false;

            Assert.Throws<InvalidOperationException>(() =>
                dlgFindPutty.CommitSettingsChanges(FolderChange(), () => persisted = true, prepared));
            Assert.False(persisted);
            Assert.AreEqual(first, App.Settings.SettingsFolder);
            Assert.AreSame(added, App.GetSessionById("Added"));
            Assert.IsNotNull(App.GetSessionById("First"));
            Assert.DoesNotThrow(App.SaveSessions);
        }

        [Test]
        public void ImportingChangedPrimaryDoesNotRefreshTheAcceptedSaveRevision()
        {
            var firstSession = App.GetSessionById("First");
            WriteCatalog(first, "First", "other-instance.invalid");
            var before = File.ReadAllBytes(CatalogPath(first));

            Assert.ThrowsAsync<SharedFile.ChangedException>(async () =>
                await App.ImportSessionsFromFileAsync(CatalogPath(first), CancellationToken.None));

            Assert.AreEqual(1, App.GetAllSessions().Count);
            Assert.AreSame(firstSession, App.GetSessionById("First"));
            Assert.AreEqual("original.invalid", firstSession.Host);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(CatalogPath(first)));
            Assert.Throws<SharedFile.ChangedException>(App.SaveSessions);
        }
    }
}
