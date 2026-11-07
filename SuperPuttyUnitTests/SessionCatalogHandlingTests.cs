using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using WeifenLuo.WinFormsUI.Docking;
using App = SuperPutty.SuperPuTTY;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class SessionCatalogHandlingTests
    {
        private List<SessionData> original;
        private string originalFolder;
        private string directory;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            original = App.GetAllSessions();
            originalFolder = App.Settings.SettingsFolder;
            App.ReplaceSessionCatalog(new SessionData[0]);
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-SessionCatalog-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            App.Settings.SettingsFolder = directory;
        }

        [TearDown]
        public void TearDown()
        {
            App.ReplaceSessionCatalog(original);
            App.Settings.SettingsFolder = originalFolder;
            Directory.Delete(directory, true);
        }

        private static SessionData Session(string id)
        {
            return new SessionData { SessionName = SessionData.GetSessionNameFromId(id), SessionId = id, Host = "example.invalid" };
        }
        private static TreeNode Root(SessionTreeview view)
        {
            return (TreeNode)typeof(SessionTreeview).GetField("nodeRoot", Private).GetValue(view);
        }
        private static void Filter(SessionTreeview view, string query)
        {
            typeof(SessionTreeview).GetMethod("ApplySearch", Private).Invoke(view, new object[] { query });
        }
        private static int Callbacks(SessionData session, object view)
        {
            return session.OnPropertyChanged == null ? 0 : session.OnPropertyChanged.GetInvocationList().Count(d => ReferenceEquals(d.Target, view));
        }

        [TestCase("<SessionData/>")]
        [TestCase("<SessionData SessionId='MissingName'/>")]
        [TestCase("<SessionData SessionId='Folder//Child' SessionName='Child'/>")]
        [TestCase("<SessionData SessionId='Other' SessionName='Shared'/>")]
        [TestCase("<SessionData SessionId='Folder/Other' SessionName='Shared'/>")]
        [TestCase("<SessionData SessionId='Valid' SessionName='Valid'/>")]
        [TestCase("<SessionData SessionId='valid' SessionName='valid'/>")]
        public void InvalidReloadPreservesOriginalCatalogAndTree(string invalid)
        {
            var saved = Session("Original"); App.AddSession(saved);
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                File.WriteAllText(Path.Combine(directory, "Sessions.XML"),
                    "<ArrayOfSessionData><SessionData SessionId='Valid' SessionName='Valid'/>" + invalid + "</ArrayOfSessionData>");
                Assert.DoesNotThrow(App.LoadSessions);
                Assert.AreEqual(1, App.GetAllSessions().Count);
                Assert.AreSame(saved, App.GetSessionById("Original"));
                Assert.AreSame(saved, Root(view).Nodes["Original"].Tag);
                Assert.AreEqual(1, Callbacks(saved, view));
            }
        }

        [Test]
        public void ValidReloadRefreshesOpenTreeAndReleasesOldSessions()
        {
            var old = Session("Old"); App.AddSession(old);
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                File.WriteAllText(Path.Combine(directory, "Sessions.XML"),
                    "<ArrayOfSessionData><SessionData SessionId='New' SessionName='New'/></ArrayOfSessionData>");
                App.LoadSessions();
                Assert.IsNull(App.GetSessionById("Old"));
                Assert.IsNotNull(Root(view).Nodes["New"]);
                Assert.AreEqual(0, Callbacks(old, view));
                Assert.AreEqual(1, Callbacks(App.GetSessionById("New"), view));
            }
        }

        [Test]
        public void SessionAndFolderPathConflictCannotReplaceCatalog()
        {
            var old = Session("Old"); App.AddSession(old);
            Assert.Throws<InvalidDataException>(() => App.ReplaceSessionCatalog(new[] { Session("Folder"), Session("Folder/Child") }));
            Assert.AreSame(old, App.GetSessionById("Old"));
        }

        [Test]
        public void CaseConflictsAreRejectedAndImportNamesBecomeUnique()
        {
            var saved = Session("Server"); Assert.IsTrue(App.AddSession(saved));
            Assert.IsFalse(App.AddSession(Session("server")));
            Assert.AreSame(saved, App.GetSessionById("SERVER"));
            var imported = Session("server");
            App.ImportSessions(new List<SessionData> { imported }, null, delegate { });
            Assert.AreEqual("server-1", imported.SessionId);
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
                Assert.AreEqual(2, Root(view).Nodes.Count);
        }

        [TestCase("Folder", "folder/Child")]
        [TestCase("Folder/Child", "folder")]
        public void AddingSessionCannotHideAnotherSessionAsAFolder(string existing, string added)
        {
            var saved = Session(existing);
            Assert.IsTrue(App.AddSession(saved));
            Assert.IsFalse(App.AddSession(Session(added)));
            Assert.That(App.GetAllSessions(), Is.EqualTo(new[] { saved }));
        }

        [Test]
        public void FilteredFolderMoveIncludesHiddenSessions()
        {
            var visible = Session("Folder/Visible"); var hidden = Session("Folder/Nested/Hidden");
            App.ReplaceSessionCatalog(new[] { visible, hidden });
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                Filter(view, "Visible");
                var root = Root(view); var target = root.Nodes.Add("Target", "Target");
                Assert.IsTrue(view.MoveSessionNode(root.Nodes["Folder"], target));
                Assert.AreEqual("Target/Folder/Visible", visible.SessionId);
                Assert.AreEqual("Target/Folder/Nested/Hidden", hidden.SessionId);
                Assert.IsNull(App.GetSessionById("Folder/Nested/Hidden"));
                Assert.AreSame(hidden, App.GetSessionById("target/folder/nested/hidden"));
                Filter(view, "");
                Assert.IsNotNull(Root(view).Nodes["Target"].Nodes["Folder"].Nodes["Nested"].Nodes["Hidden"]);
            }
        }

        [Test]
        public void FilteredFolderRenameAndDeleteIncludeHiddenSessions()
        {
            var visible = Session("Folder/Visible"); var hidden = Session("Folder/Hidden"); var other = Session("Other/Keep");
            App.ReplaceSessionCatalog(new[] { visible, hidden, other });
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                Filter(view, "Visible");
                view.RenameFolderNode(Root(view).Nodes["Folder"], "Renamed");
                Assert.AreEqual("Renamed/Hidden", hidden.SessionId);
                Assert.AreEqual(2, App.GetSessionsInFolder("Renamed").Count);
                App.RemoveSessionFolder("Renamed");
                Assert.That(App.GetAllSessions(), Is.EqualTo(new[] { other }));
                Assert.IsNull(Root(view).Nodes["Renamed"]);
            }
        }

        [Test]
        public void HiddenDestinationConflictLeavesCatalogAndTreeUnchanged()
        {
            var visible = Session("Folder/Visible"); var hidden = Session("Taken/Hidden");
            App.ReplaceSessionCatalog(new[] { visible, hidden });
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                Filter(view, "Visible");
                Assert.Throws<InvalidOperationException>(() => view.RenameFolderNode(Root(view).Nodes["Folder"], "Taken"));
                Assert.AreEqual("Folder/Visible", visible.SessionId);
                Assert.AreEqual("Taken/Hidden", hidden.SessionId);
                Assert.IsNotNull(Root(view).Nodes["Folder"]);
            }
        }

        [Test]
        public void ModelMoveRollsBackCanceledSessionIdChanges()
        {
            var first = Session("Folder/A"); var second = Session("Folder/B");
            App.ReplaceSessionCatalog(new[] { first, second });
            second.OnPropertyChanging += delegate(SessionData s, string property, object value, ref bool cancel)
            { if (property == "SessionId" && (string)value == "Target/B") cancel = true; };
            Assert.Throws<InvalidOperationException>(() => App.MoveSessionFolder("Folder", "Target"));
            Assert.AreEqual("Folder/A", first.SessionId);
            Assert.AreEqual("Folder/B", second.SessionId);
            Assert.AreSame(first, App.GetSessionById("Folder/A"));
            Assert.IsNull(App.GetSessionById("Target/A"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RebuildsKeepOneSubscriptionAndClosingRemovesIt(bool close)
        {
            var visible = Session("Folder/Visible"); var hidden = Session("Folder/Hidden");
            App.ReplaceSessionCatalog(new[] { visible, hidden });
            using (var dock = new DockPanel())
            {
                var view = new SessionTreeview(dock);
                try
                {
                    for (int i = 0; i < 12; i++) Filter(view, i % 2 == 0 ? "Visible" : "");
                    Assert.AreEqual(1, Callbacks(visible, view));
                    Assert.AreEqual(1, Callbacks(hidden, view));
                }
                finally { if (close) view.Close(); else view.Dispose(); }
                Assert.AreEqual(0, Callbacks(visible, view));
                Assert.AreEqual(0, Callbacks(hidden, view));
                Assert.IsNull(visible.OnPropertyChanging);
                App.RemoveSession(visible.SessionId);
                Assert.DoesNotThrow(() => App.AddSession(Session("AfterClose")));
            }
        }

        [Test]
        public void LeafMoveEditAndDeleteKeepCatalogTreeAndSubscriptionsInSync()
        {
            var session = Session("Folder/Server");
            App.ReplaceSessionCatalog(new[] { session, Session("Target/Existing") });
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                var root = Root(view);
                Assert.IsTrue(view.MoveSessionNode(root.Nodes["Folder"].Nodes["Server"], root.Nodes["Target"]));
                Assert.AreEqual("Target/Server", session.SessionId);
                Assert.IsNull(App.GetSessionById("Folder/Server"));
                var edited = (SessionData)session.Clone();
                edited.SessionId = "Target/Renamed";
                edited.SessionName = "Renamed";
                session.CopyFrom(edited);
                Assert.AreSame(session, App.GetSessionById("target/renamed"));
                Assert.IsNull(App.GetSessionById("Target/Server"));
                Assert.AreSame(session, Root(view).Nodes["Target"].Nodes["Renamed"].Tag);
                Assert.AreEqual(2, App.GetAllSessions().Count);
                Assert.AreEqual(1, Callbacks(session, view));
                App.RemoveSession("target/renamed");
                Assert.IsNull(Root(view).Nodes["Target"].Nodes["Renamed"]);
                Assert.AreEqual(0, Callbacks(session, view));
            }
        }

        [Test]
        public void HiddenSessionNameConflictIsRejectedBeforeEditing()
        {
            App.ReplaceSessionCatalog(new[] { Session("Folder/Visible"), Session("Folder/Hidden") });
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                Filter(view, "Visible");
                var folder = Root(view).Nodes["Folder"];
                object[] args = { folder, folder.Nodes["Visible"], "hidden", null };
                Assert.IsFalse((bool)typeof(SessionTreeview).GetMethod("ValidateSessionNameChange", Private).Invoke(view, args));
                Assert.IsNotEmpty((string)args[3]);
                args = new object[] { folder, folder.Nodes["Visible"], "visible", null };
                Assert.IsTrue((bool)typeof(SessionTreeview).GetMethod("ValidateSessionNameChange", Private).Invoke(view, args));
            }
        }

        [Test]
        public void FolderMoveAndCaseOnlyRenamePreserveExpandedDescendants()
        {
            App.ReplaceSessionCatalog(new[] { Session("Folder/Nested/Server"), Session("Target/Existing"), Session("Other/Keep") });
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                var tree = (TreeView)typeof(SessionTreeview).GetField("treeView1", Private).GetValue(view);
                var handle = tree.Handle;
                Root(view).ExpandAll();
                Assert.IsTrue(Root(view).Nodes["Folder"].Nodes["Nested"].IsExpanded);
                Assert.IsTrue(view.MoveSessionNode(Root(view).Nodes["Folder"], Root(view).Nodes["Target"]));
                Assert.IsTrue(Root(view).Nodes["Target"].Nodes["Folder"].Nodes["Nested"].IsExpanded);
                Assert.IsTrue(Root(view).Nodes["Other"].IsExpanded);
                view.RenameFolderNode(Root(view).Nodes["Target"].Nodes["Folder"], "folder");
                Assert.AreEqual("Target/folder/Nested/Server", App.GetSessionById("Target/folder/Nested/Server").SessionId);
                Assert.IsTrue(Root(view).Nodes["Target"].Nodes["folder"].Nodes["Nested"].IsExpanded);
            }
        }

        [Test]
        public void LegacyMovedFolderHelperAlsoUpdatesHiddenCatalogEntries()
        {
            var visible = Session("Folder/Visible"); var hidden = Session("Folder/Hidden");
            App.ReplaceSessionCatalog(new[] { visible, hidden });
            using (var dock = new DockPanel())
            using (var view = new SessionTreeview(dock))
            {
                Filter(view, "Visible"); var root = Root(view); var folder = root.Nodes["Folder"];
                var target = root.Nodes.Add("Target", "Target"); folder.Remove(); target.Nodes.Add(folder);
                view.resetFoldersChildrenPaths(folder);
                Assert.AreEqual("Target/Folder/Hidden", hidden.SessionId);
            }
        }
    }
}
