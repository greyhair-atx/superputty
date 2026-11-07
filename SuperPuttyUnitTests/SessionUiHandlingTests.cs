using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Scp;
using SuperPutty.Data;
using SuperPutty.Utils;
using WeifenLuo.WinFormsUI.Docking;
using App = SuperPutty.SuperPuTTY;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class SessionUiHandlingTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private List<SessionData> original;
        private string originalFolder;
        private string directory;
        private bool originalDocumentRestriction;
        private bool originalFloatRestriction;
        private string SessionsFile { get { return Path.Combine(directory, "Sessions.XML"); } }

        [SetUp]
        public void SetUp()
        {
            original = App.GetAllSessions();
            originalFolder = App.Settings.SettingsFolder;
            originalDocumentRestriction = App.Settings.RestrictContentToDocumentTabs;
            originalFloatRestriction = App.Settings.DockingRestrictFloatingWindows;
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-SessionUi-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            App.Settings.SettingsFolder = directory;
            App.ReplaceSessionCatalog(new SessionData[0]);
        }

        [TearDown]
        public void TearDown()
        {
            App.ReplaceSessionCatalog(original);
            App.Settings.SettingsFolder = originalFolder;
            App.Settings.RestrictContentToDocumentTabs = originalDocumentRestriction;
            App.Settings.DockingRestrictFloatingWindows = originalFloatRestriction;
            Directory.Delete(directory, true);
        }

        private static SessionData Session(string id)
        {
            return new SessionData { SessionId = id, SessionName = SessionData.GetSessionNameFromId(id), Host = "example.invalid" };
        }

        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, Private).GetValue(instance);
        }

        private static TreeNode Root(SessionTreeview view) { return Field<TreeNode>(view, "nodeRoot"); }
        private static PropertyGrid Grid(SessionDetail detail) { return Field<PropertyGrid>(detail, "sessionDetailPropertyGrid"); }
        private static int Callbacks(Delegate handlers, object target)
        {
            return handlers == null ? 0 : handlers.GetInvocationList().Count(d => ReferenceEquals(d.Target, target));
        }

        private static void Select(SessionTreeview view, TreeNode node)
        {
            var tree = Field<TreeView>(view, "treeView1");
            var handle = tree.Handle;
            tree.SelectedNode = node;
        }

        [TestCase(false, "Taken")]
        [TestCase(true, "Taken")]
        [TestCase(false, "taken")]
        [TestCase(true, "taken")]
        [TestCase(false, "Existing")]
        [TestCase(true, "Existing")]
        public void SaveCurrentRejectsSessionAndFolderConflictsBeforeChangingAnything(bool treeOpen, string name)
        {
            var session = Session("Original");
            App.ReplaceSessionCatalog(new[] { session, Session("Taken/Child"), Session("Existing") });
            App.SaveSessions();
            string before = File.ReadAllText(SessionsFile);
            using (var dock = new DockPanel())
            using (var view = treeOpen ? new SessionTreeview(dock) : null)
            {
                var edited = Session(name);
                bool persisted = false;
                Assert.IsTrue(App.SessionPathConflicts(name, session));
                Assert.Throws<InvalidDataException>(() => App.SaveSessionChanges(session, edited, () => persisted = true));
                Assert.IsFalse(persisted);
                Assert.AreEqual("Original", session.SessionId);
                Assert.AreEqual("Original", session.SessionName);
                Assert.AreSame(session, App.GetSessionById("Original"));
                Assert.AreEqual(3, App.GetAllSessions().Count);
                Assert.AreEqual(before, File.ReadAllText(SessionsFile));
                if (view != null) Assert.AreSame(session, Root(view).Nodes["Original"].Tag);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void FailedSaveRestoresFieldsIndexAndTreeForSavedAndNewSessions(bool saved, bool treeOpen)
        {
            var session = Session(saved ? "Original" : "ConnectBar/Original");
            if (saved) App.AddSession(session);
            App.SaveSessions();
            string before = File.ReadAllText(SessionsFile);
            using (var dock = new DockPanel())
            using (var view = treeOpen ? new SessionTreeview(dock) : null)
            {
                var edited = Session("Renamed"); edited.Host = "changed.invalid";
                Assert.Throws<IOException>(() => App.SaveSessionChanges(session, edited, () => { throw new IOException("Test write failure"); }));
                Assert.AreEqual(saved ? "Original" : "ConnectBar/Original", session.SessionId);
                Assert.AreEqual("Original", session.SessionName);
                Assert.AreEqual("example.invalid", session.Host);
                Assert.IsNull(App.GetSessionById("Renamed"));
                Assert.AreEqual(saved ? 1 : 0, App.GetAllSessions().Count);
                if (saved) Assert.AreSame(session, App.GetSessionById("Original"));
                Assert.AreEqual(before, File.ReadAllText(SessionsFile));
                Assert.IsFalse(App.IsUpdatingSessionCatalog);
                if (view != null) Assert.AreEqual(saved ? 1 : 0, Root(view).Nodes.Count);
            }
        }

        [TestCase(false, "Renamed")]
        [TestCase(true, "Renamed")]
        [TestCase(true, "original")]
        public void SuccessfulSavePreservesSessionIdentityAndPersistsTheFinalName(bool saved, string name)
        {
            var session = Session(saved ? "Original" : "ConnectBar/Original");
            if (saved) App.AddSession(session);
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                Assert.IsFalse(App.SessionPathConflicts(name, session));
                App.SaveSessionChanges(session, Session(name), App.SaveSessions);
                Assert.AreSame(session, App.GetSessionById(name));
                Assert.AreSame(session, Root(view).Nodes[name].Tag);
                Assert.AreEqual(1, App.GetAllSessions().Count);
                var restored = SessionData.LoadSessionsFromFile(SessionsFile).Single();
                Assert.AreEqual(name, restored.SessionId);
                Assert.AreEqual(name, restored.SessionName);
                Assert.AreEqual(1, Callbacks(session.OnPropertyChanged, view));
            }
        }

        [Test]
        public void CanceledCurrentSessionRenameRollsBackWithoutPersisting()
        {
            var session = Session("Original"); App.AddSession(session);
            session.OnPropertyChanging += delegate(SessionData s, string property, object value, ref bool cancel)
            { if (property == "SessionId" && (string)value == "Renamed") cancel = true; };
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                bool persisted = false;
                Assert.Throws<InvalidOperationException>(() => App.SaveSessionChanges(session, Session("Renamed"), () => persisted = true));
                Assert.IsFalse(persisted);
                Assert.AreEqual("Original", session.SessionId);
                Assert.AreEqual("Original", session.SessionName);
                Assert.AreSame(session, App.GetSessionById("Original"));
                Assert.AreSame(session, Root(view).Nodes["Original"].Tag);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClosingOrDisposingTreeFlushesAPendingFolderMove(bool close)
        {
            var session = Session("Folder/Server"); App.AddSession(session); App.SaveSessions();
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                var root = Root(view); var target = root.Nodes.Add("Target", "Target");
                Assert.IsTrue(view.MoveSessionNode(root.Nodes["Folder"], target));
                view.ScheduleSessionSave();
                if (close) view.Close(); else view.Dispose();
                Assert.AreEqual("Target/Folder/Server", SessionData.LoadSessionsFromFile(SessionsFile).Single().SessionId);
                Assert.AreEqual(0, Callbacks(session.OnPropertyChanged, view));
            }
        }

        [Test]
        public void FailedPendingSaveKeepsChangesForALaterFlush()
        {
            var session = Session("Folder/Server"); App.AddSession(session); App.SaveSessions();
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                view.RenameFolderNode(Root(view).Nodes["Folder"], "Moved");
                view.ScheduleSessionSave();
                using (var locked = new FileStream(SessionsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Assert.IsFalse(view.FlushPendingSessionSave());
                    Assert.AreEqual("Folder/Server", SessionData.LoadSessionsFromFile(SessionsFile).Single().SessionId);
                }
                Assert.IsTrue(view.FlushPendingSessionSave());
                Assert.AreEqual("Moved/Server", SessionData.LoadSessionsFromFile(SessionsFile).Single().SessionId);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DetailsClearsSelectionWhenSelectedSessionIsHiddenOrDeleted(bool deleted)
        {
            var session = Session("Folder/Selected"); App.AddSession(session);
            using (var dock = new DockPanel())
            {
                var helper = new SingletonToolWindowHelper<SessionTreeview>("Sessions", dock, null, h => new SessionTreeview(dock));
                using (var view = helper.Initialize())
                using (var detail = new SessionDetail(helper))
                {
                    Select(view, Root(view).Nodes["Folder"].Nodes["Selected"]);
                    Assert.AreSame(session, Grid(detail).SelectedObject);
                    if (deleted) App.RemoveSession(session.SessionId);
                    else typeof(SessionTreeview).GetMethod("ApplySearch", Private).Invoke(view, new object[] { "NoResults" });
                    Assert.IsNull(view.SelectedSession);
                    Assert.IsNull(Grid(detail).SelectedObject);
                    Assert.AreEqual(0, Callbacks(session.OnPropertyChanged, detail));
                }
            }
        }

        [Test]
        public void DetailsRebindsToReplacementSessionAndClearsOnTreeDisposal()
        {
            var session = Session("Selected"); App.AddSession(session);
            using (var dock = new DockPanel())
            {
                var helper = new SingletonToolWindowHelper<SessionTreeview>("Sessions", dock, null, h => new SessionTreeview(dock));
                using (var view = helper.Initialize())
                using (var detail = new SessionDetail(helper))
                {
                    Select(view, Root(view).Nodes["Selected"]);
                    var replacement = Session("Selected");
                    App.ReplaceSessionCatalog(new[] { replacement });
                    Assert.AreSame(replacement, Grid(detail).SelectedObject);
                    Assert.AreEqual(0, Callbacks(session.OnPropertyChanged, detail));
                    view.Dispose();
                    Assert.IsNull(Grid(detail).SelectedObject);
                    Assert.AreEqual(0, Callbacks(replacement.OnPropertyChanged, detail));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DetailsReleasesSessionTreeAndHelperSubscriptionsOnDisposeOrClose(bool close)
        {
            var session = Session("Selected"); App.AddSession(session);
            using (var dock = new DockPanel())
            {
                var helper = new SingletonToolWindowHelper<SessionTreeview>("Sessions", dock, null, h => new SessionTreeview(dock));
                using (var view = helper.Initialize())
                using (var detail = new SessionDetail(helper))
                {
                    Select(view, Root(view).Nodes["Selected"]);
                    Assert.AreEqual(1, Callbacks(session.OnPropertyChanged, detail));
                    if (close) detail.Close(); else detail.Dispose();
                    Assert.AreEqual(0, Callbacks(session.OnPropertyChanged, detail));
                    Assert.AreEqual(0, Callbacks(Field<Delegate>(helper, "InstanceChanged"), detail));
                    Assert.AreEqual(0, Callbacks(Field<Delegate>(view, "SelectionChanged"), detail));
                    session.Host = "changed.invalid";
                    Assert.DoesNotThrow(() => App.RemoveSession(session.SessionId));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SwitchSelectorSupportsScpAndSkipsDisposedOrUnknownDocuments(bool disposedTerminal)
        {
            using (var terminal = new ctlPuttyPanel(Session("Terminal"), null))
            using (var scp = new PscpBrowserPanel())
            using (var unknown = new ToolWindowDocument())
            {
                terminal.AppPanel.ApplicationName = String.Empty;
                var session = Session("Files"); session.Proto = ConnectionProtocol.SCP;
                typeof(PscpBrowserPanel).GetProperty("Session").GetSetMethod(true).Invoke(scp, new object[] { session });
                scp.Text = "File transfers";
                if (disposedTerminal) terminal.Dispose();
                using (var data = frmSuperPutty.BuildSwitchSessionData(new IDockContent[] { terminal, scp, unknown }))
                {
                    Assert.AreEqual(disposedTerminal ? 1 : 2, data.ItemData.Count);
                    var row = data.ItemData.Single(r => ReferenceEquals(r.Tag, scp));
                    Assert.AreEqual("File transfers", row.Name);
                    Assert.AreEqual("Files", row.Detail);
                }
            }
        }

        [Test]
        public void NewSessionMenuPreservesRepeatedNamesAndCaseInsensitiveFolders()
        {
            var direct = Session("Prod/Prod"); var nested = Session("Prod/Nested/Prod"); var other = Session("prod/Other");
            App.ReplaceSessionCatalog(new[] { direct, nested, other });
            using (var panel = new ctlPuttyPanel(Session("Menu"), null))
            {
                panel.AppPanel.ApplicationName = String.Empty;
                var initialize = typeof(ctlPuttyPanel).GetMethod("InitNewSessionToolStripMenuItems", Private);
                initialize.Invoke(panel, null);
                var menu = Field<ToolStripMenuItem>(panel, "newSessionToolStripMenuItem");
                Assert.AreEqual(1, menu.DropDownItems.Count);
                var folder = (ToolStripMenuItem)menu.DropDownItems["Prod"];
                Assert.AreEqual(3, folder.DropDownItems.Count);
                Assert.AreSame(direct, folder.DropDownItems["Prod"].Tag);
                Assert.AreSame(nested, ((ToolStripMenuItem)folder.DropDownItems["Nested"]).DropDownItems["Prod"].Tag);
                Assert.AreSame(other, folder.DropDownItems["Other"].Tag);
                int disposed = 0;
                folder.Disposed += (s, e) => disposed++;
                folder.DropDownItems["Prod"].Disposed += (s, e) => disposed++;
                initialize.Invoke(panel, null);
                Assert.AreEqual(2, disposed);
                Assert.AreEqual(1, menu.DropDownItems.Count);
            }
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void DockingRestrictionsAreStableAndRestoreOriginalPermissions(bool documentsOnly, bool restrictFloat, bool originallyFloat)
        {
            using (var content = new DockContent())
            {
                DockAreas originalAreas = DockAreas.Document | DockAreas.DockLeft | (originallyFloat ? DockAreas.Float : 0);
                content.DockAreas = originalAreas;
                App.Settings.RestrictContentToDocumentTabs = documentsOnly;
                App.Settings.DockingRestrictFloatingWindows = restrictFloat;
                DockAreas expected = documentsOnly ? originalAreas & (DockAreas.Document | DockAreas.Float) : originalAreas;
                if (restrictFloat) expected &= ~DockAreas.Float;
                for (int i = 0; i < 3; i++)
                {
                    App.ApplyDockRestrictions(content);
                    Assert.AreEqual(expected, content.DockAreas);
                }
                App.Settings.RestrictContentToDocumentTabs = false;
                App.Settings.DockingRestrictFloatingWindows = false;
                App.ApplyDockRestrictions(content);
                Assert.AreEqual(originalAreas, content.DockAreas);
            }
        }
    }
}
