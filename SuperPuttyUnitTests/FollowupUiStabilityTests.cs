using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using WeifenLuo.WinFormsUI.Docking;
using App = SuperPutty.SuperPuTTY;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class FollowupUiStabilityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class SessionPanel : ctlPuttyPanel
        {
            internal SessionPanel(SessionData session) : base(session, null) { }
            protected override void OnLoad(EventArgs e) { } // No external process in this fixture.
        }

        [Test]
        public void ExitedTabClosesEvenWhenDockPositionCannotBeSaved()
        {
            var session = new SessionData { SessionId = "test", SessionName = "test", Host = "test",
                Proto = ConnectionProtocol.SSH, LastDockstate = DockState.DockLeft };
            using (var theme = new VS2015LightTheme())
            using (var docking = new DockPanel { Theme = theme })
            using (var panel = new SessionPanel(session))
            {
                panel.AppPanel.ApplicationName = String.Empty;
                panel.DockPanel = docking;
                panel.DockState = DockState.DockRight;
                var handle = panel.Handle;
                int attempts = 0;
                App.CloseExitedSession(panel, session, () => { attempts++; throw new IOException("Read-only settings"); });
                Assert.AreEqual(1, attempts);
                Assert.IsTrue(panel.IsDisposed);
                Assert.AreEqual(DockState.DockLeft, session.LastDockstate);
                Assert.DoesNotThrow(() => App.CloseExitedSession(panel, session, () => Assert.Fail("Already closed")));
            }
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
