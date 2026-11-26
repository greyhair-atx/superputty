using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Scp;
using WeifenLuo.WinFormsUI.Docking;
using App = SuperPutty.SuperPuTTY;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class FollowupUiStabilityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void MissingTerminalExecutableAllowsRetryAndCloseInTheSameTab()
        {
            string originalPath = App.Settings.PuttyExe;
            App.Settings.PuttyExe = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
            try
            {
                var session = new SessionData { SessionId = "test", SessionName = "test", Host = "test",
                    Proto = ConnectionProtocol.SSH, LastDockstate = DockState.DockLeft };
                using (var panel = new ctlPuttyPanel(session, null))
                {
                    panel.Show();
                    panel.WatchStartup();
                    var firstClient = panel.AppPanel;
                    PumpUntil(() => panel.Startup.IsCompleted);
                    StringAssert.Contains("executable is missing", panel.Startup.Result);
                    Assert.IsTrue(firstClient.IsDisposed);
                    Assert.IsFalse(panel.IsDisposed);

                    panel.RetryStartup();
                    Assert.AreNotSame(firstClient, panel.AppPanel);
                    PumpUntil(() => panel.Startup.IsCompleted);
                    Assert.IsFalse(panel.IsDisposed);
                    Assert.IsNotNull(panel.Startup.Result);
                    panel.Close();
                    Assert.IsTrue(panel.IsDisposed);
                    Assert.AreEqual(DockState.DockLeft, session.LastDockstate);
                }
            }
            finally { App.Settings.PuttyExe = originalPath; }
        }

        [Test]
        public void TerminalAndScpTabsKeepTheirConnectionAfterASavedProfileEdit()
        {
            var original = App.GetAllSessions();
            var session = new SessionData { SessionId = "test", SessionName = "test", Host = "first.invalid",
                Port = 2222, Username = "first", Proto = ConnectionProtocol.SSH, RemotePath = "/" };
            try
            {
                App.ReplaceSessionCatalog(new[] { session });
                var options = new PscpOptions { PscpLocation = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe") };
                using (var terminal = new ctlPuttyPanel(session, null))
                using (var browser = new PscpBrowserPanel(session, options, Path.GetTempPath()))
                {
                    session.Host = "second.invalid";
                    session.Port = 2200;
                    session.Proto = ConnectionProtocol.Telnet;
                    Assert.AreSame(session, terminal.GetCurrentSavedSession());
                    Assert.AreSame(session, browser.GetCurrentSavedSession());
                    Assert.AreEqual("first.invalid", terminal.Session.Host);
                    Assert.AreEqual(ConnectionProtocol.SSH, terminal.Session.Proto);
                    var remote = (BrowserPresenter)typeof(PscpBrowserPanel).GetField("remoteBrowserPresenter", Private).GetValue(browser);
                    Assert.AreSame(browser.Session, remote.Session);
                    Assert.AreEqual("first.invalid", remote.Session.Host);
                    Assert.AreEqual(2222, remote.Session.Port);
                    remote.Session.Username = "tab-auth";
                    var transfer = new FileTransfer(options, new FileTransferRequest { Session = remote.Session });
                    remote.Session.Username = "next-auth";
                    Assert.AreEqual("tab-auth", transfer.Request.Session.Username);
                    Assert.AreEqual("first", session.Username);
                    using (var reopened = new ctlPuttyPanel(session, null))
                        Assert.AreEqual("second.invalid", reopened.Session.Host);
                }
            }
            finally { App.ReplaceSessionCatalog(original); }
        }

        [Test]
        public void ConnectAllLimitsConcurrentStartsAndSkipsPendingTabsWhenCanceled()
        {
            var sessions = Enumerable.Range(1, 8).Select(index => new SessionData { SessionId = "test/" + index }).ToArray();
            var launches = new System.Collections.Generic.List<TaskCompletionSource<SessionLaunchResult>>();
            using (var cancellation = new CancellationTokenSource())
            {
                var result = SessionTreeview.RunConnectAllAsync(sessions, session =>
                {
                    var launch = new TaskCompletionSource<SessionLaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                    launches.Add(launch);
                    return launch.Task;
                }, TimeSpan.Zero, cancellation.Token);

                Assert.AreEqual(SessionTreeview.ConnectAllParallelStartups, launches.Count);
                launches[0].SetResult(SessionLaunchResult.Completed("Missing client"));
                PumpUntil(() => launches.Count == SessionTreeview.ConnectAllParallelStartups + 1);
                cancellation.Cancel();
                foreach (var launch in launches.Skip(1)) launch.SetResult(SessionLaunchResult.Completed(null));
                PumpUntil(() => result.IsCompleted);
                Assert.AreEqual(3, result.Result.Opened);
                Assert.AreEqual(4, result.Result.Canceled);
                CollectionAssert.AreEqual(new[] { "test/1: Missing client" }, result.Result.Failures);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ConnectAllPausesDuringLayoutConfirmationAndResumesOnlyIfCanceled(bool approve)
        {
            var sessions = Enumerable.Range(1, 5).Select(index => new SessionData { SessionId = "test/" + index }).ToArray();
            var launches = new System.Collections.Generic.List<TaskCompletionSource<SessionLaunchResult>>();
            var batch = SessionTreeview.RunConnectAllAsync(sessions, session =>
            {
                var launch = new TaskCompletionSource<SessionLaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                launches.Add(launch);
                return launch.Task;
            }, TimeSpan.Zero, CancellationToken.None);

            var initialLaunches = launches.ToArray();
            Assert.AreEqual(SessionTreeview.ConnectAllParallelStartups, initialLaunches.Length);
            LayoutData original = App.CurrentLayout;
            EventHandler<LayoutChangedEventArgs> confirm = (sender, e) =>
            {
                initialLaunches[0].SetResult(SessionLaunchResult.Completed(null));
                var elapsed = Stopwatch.StartNew();
                while (elapsed.ElapsedMilliseconds < 150)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                Assert.AreEqual(SessionTreeview.ConnectAllParallelStartups, launches.Count);
                e.Cancel = !approve;
            };
            App.LayoutChanging += confirm;
            try { App.LoadLayout(new LayoutData("Other.xml")); }
            finally { App.LayoutChanging -= confirm; }

            try
            {
                // Canceling the layout can already have started a queued launch.
                foreach (var launch in initialLaunches.Skip(1))
                    launch.SetResult(SessionLaunchResult.Completed(null));
                if (!approve)
                {
                    PumpUntil(() => launches.Count == sessions.Length);
                    foreach (var launch in launches.Skip(initialLaunches.Length))
                        launch.SetResult(SessionLaunchResult.Completed(null));
                }
                PumpUntil(() => batch.IsCompleted);
                Assert.AreEqual(approve ? 3 : 5, batch.Result.Opened);
                Assert.AreEqual(approve ? 2 : 0, batch.Result.Canceled);
            }
            finally { App.LoadLayout(original, true); }
        }

        [Test]
        public void SavesOnlyDockHistoryAndMergesChangesFromTwoInstances()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SuperPuttyDockHistory-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string catalog = Path.Combine(folder, "Sessions.XML");
            string history = SessionDockingHistory.FilePath(folder);
            try
            {
                File.WriteAllText(catalog, "Session data");
                var first = new SessionDockingHistory();
                var second = new SessionDockingHistory();
                Assert.AreEqual(DockState.DockTop, first.Get(folder, "legacy", DockState.DockTop));
                first.Remember(folder, "First", DockState.DockLeft, DockState.Document);
                second.Remember(folder, "Second", DockState.DockRight, DockState.Document);
                second.Remember(folder, "Hidden", DockState.Hidden, DockState.Document);
                first.Save();
                second.Save();

                var reopened = new SessionDockingHistory();
                Assert.AreEqual(DockState.DockLeft, reopened.Get(folder, "first", DockState.Document));
                Assert.AreEqual(DockState.DockRight, reopened.Get(folder, "Second", DockState.Document));
                Assert.AreEqual(DockState.Document, reopened.Get(folder, "Hidden", DockState.Document));
                Assert.AreEqual("Session data", File.ReadAllText(catalog));
            }
            finally
            {
                File.Delete(history);
                File.Delete(catalog);
                Directory.Delete(folder);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SavingDockHistoryPreservesDamagedOriginalAndRestoresNewPositions(bool oversized)
        {
            string folder = Path.Combine(Path.GetTempPath(), "SuperPuttyDockRecovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string history = SessionDockingHistory.FilePath(folder);
            string damaged = oversized ? new string('x', 2 * 1024 * 1024 + 1) : "<SessionDocking>";
            try
            {
                File.WriteAllText(history, damaged);
                var prior = new SessionDockingHistory();
                Assert.AreEqual(DockState.Document, prior.Get(folder, "Probe", DockState.Document));
                prior.Remember(folder, "Probe", DockState.DockRight, DockState.Document);
                prior.Save();

                var reopened = new SessionDockingHistory();
                Assert.AreEqual(DockState.DockRight, reopened.Get(folder, "Probe", DockState.Document));
                var preserved = Directory.GetFiles(folder, "SessionDocking.xml.corrupt.*").Single();
                Assert.AreEqual(damaged, File.ReadAllText(preserved));
            }
            finally
            {
                foreach (string file in Directory.EnumerateFiles(folder)) File.Delete(file);
                Directory.Delete(folder);
            }
        }

        private static void PumpUntil(Func<bool> condition)
        {
            var elapsed = Stopwatch.StartNew();
            while (!condition() && elapsed.ElapsedMilliseconds < 5000)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Assert.IsTrue(condition(), "The operation did not complete.");
        }

        private sealed class Detail : SessionDetail
        {
            internal string Error;
            internal Detail() : base(null) { }
            protected override void ShowSaveError(string message) { Error = message; }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SessionDetailCommitsOnlyAfterPersistenceSucceeds(bool rename)
        {
            var original = App.GetAllSessions();
            var session = new SessionData { SessionId = "folder/original", SessionName = "original", Host = "old" };
            try
            {
                App.ReplaceSessionCatalog(new[] { session });
                using (var detail = new Detail())
                {
                    typeof(SessionDetail).GetMethod("SelectedSessionChanged", Private).Invoke(detail, new[] { session });
                    var grid = (PropertyGrid)typeof(SessionDetail).GetField("sessionDetailPropertyGrid", Private).GetValue(detail);
                    var draft = (SessionData)grid.SelectedObject;
                    draft.Host = "new";
                    if (rename) { draft.SessionName = "renamed"; draft.SessionId = "folder/renamed"; }
                    Assert.AreEqual("old", session.Host);
                    Assert.IsFalse(detail.CommitChanges(() => { throw new IOException("Disk unavailable"); }));
                    Assert.IsNotNull(detail.Error);
                    Assert.AreEqual("old", session.Host);
                    Assert.AreEqual("folder/original", session.SessionId);
                    Assert.AreSame(session, App.GetSessionById("folder/original"));
                    Assert.IsNull(App.GetSessionById("folder/renamed"));
                    Assert.AreEqual("old", ((SessionData)grid.SelectedObject).Host);

                    draft = (SessionData)grid.SelectedObject;
                    draft.Host = "saved";
                    if (rename) { draft.SessionName = "renamed"; draft.SessionId = "folder/renamed"; }
                    bool persisted = false;
                    Assert.IsTrue(detail.CommitChanges(() => persisted = true));
                    Assert.IsTrue(persisted);
                    Assert.AreEqual("saved", session.Host);
                    Assert.AreSame(session, App.GetSessionById(rename ? "folder/renamed" : "folder/original"));
                }
            }
            finally { App.ReplaceSessionCatalog(original); }
        }

        [Test]
        public void DirectLogViewerDisposalUnregistersClosesAndDrainsItsAppender()
        {
            var viewer = new Log4netLogViewer();
            var appender = (AppenderSkeleton)typeof(Log4netLogViewer).GetField("memoryAppender", Private).GetValue(viewer);
            var repository = (Hierarchy)typeof(Log4netLogViewer).GetField("repository", Private).GetValue(viewer);
            viewer.Dispose();
            Assert.IsFalse(repository.Root.Appenders.Cast<IAppender>().Contains(appender));
            appender.DoAppend(new LoggingEvent(typeof(FollowupUiStabilityTests), null, "test", Level.Info, "after disposal", null));
            var drain = appender.GetType().GetMethod("Drain", Private | BindingFlags.Public);
            Assert.AreEqual(String.Empty, drain.Invoke(appender, null));
            Assert.DoesNotThrow(viewer.Dispose);
            Assert.DoesNotThrow(() => typeof(Log4netLogViewer).GetMethod("timerLogPull_Tick", Private)
                .Invoke(viewer, new object[] { null, EventArgs.Empty }));
        }

        [Test]
        public void LogViewerBoundsBacklogAndHistoryAndRetainsNewestMessages()
        {
            using (var viewer = new Log4netLogViewer())
            {
                var appender = (AppenderSkeleton)typeof(Log4netLogViewer).GetField("memoryAppender", Private).GetValue(viewer);
                var tick = typeof(Log4netLogViewer).GetMethod("timerLogPull_Tick", Private);
                var text = (RichTextBox)typeof(Log4netLogViewer).GetField("richTextBoxLogMessages", Private).GetValue(viewer);
                for (int batch = 0; batch < 2; batch++)
                {
                    for (int i = 0; i < 3000; i++)
                        appender.DoAppend(new LoggingEvent(typeof(FollowupUiStabilityTests), null, "test", Level.Info,
                            "message-" + batch + "-" + i + ":" + new string('x', 200), null));
                    var queue = (ICollection)appender.GetType().GetField("messages", Private).GetValue(appender);
                    Assert.AreEqual(Log4netLogViewer.MaximumPendingMessages, queue.Count);
                    tick.Invoke(viewer, new object[] { null, EventArgs.Empty });
                    Assert.LessOrEqual(text.TextLength, Log4netLogViewer.MaximumHistoryCharacters);
                    StringAssert.Contains("message-" + batch + "-2999:", text.Text);
                    StringAssert.DoesNotContain("message-" + batch + "-0:", text.Text);
                    Assert.AreEqual(0, queue.Count);
                }
            }
        }

        [Test]
        public void RdpResizeIgnoresAnUnavailableOrDisposedActiveXControl()
        {
            RdpClientPanel panel;
            Assert.IsTrue(RdpClientPanel.TryCreate(new SessionData { Host = "test", Proto = ConnectionProtocol.RDP }, delegate { }, out panel));
            using (panel)
            {
                var resize = typeof(RdpClientPanel).GetMethod("UpdateSessionDisplaySettings", Private, null, Type.EmptyTypes, null);
                var client = (Control)typeof(RdpClientPanel).GetField("client", Private).GetValue(panel);
                using (var uninitialized = (Control)Activator.CreateInstance(client.GetType()))
                {
                    typeof(RdpClientPanel).GetField("client", Private).SetValue(panel, uninitialized);
                    try { Assert.DoesNotThrow(() => resize.Invoke(panel, null)); }
                    finally { typeof(RdpClientPanel).GetField("client", Private).SetValue(panel, client); }
                }
                client.Dispose();
                Assert.DoesNotThrow(() => resize.Invoke(panel, null));
                panel.Dispose();
                Assert.DoesNotThrow(() => resize.Invoke(panel, null));
            }
        }
    }
}
