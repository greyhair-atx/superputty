using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class SessionTabActionsTests
    {
        [TestCase(SessionConnectionState.Connected, false, true)]
        [TestCase(SessionConnectionState.Connecting, false, true)]
        [TestCase(SessionConnectionState.Reconnecting, false, true)]
        [TestCase(SessionConnectionState.Unknown, false, true)]
        [TestCase(SessionConnectionState.Connected, true, false)]
        [TestCase(SessionConnectionState.Connecting, true, false)]
        [TestCase(SessionConnectionState.Reconnecting, true, false)]
        [TestCase(SessionConnectionState.Unknown, true, false)]
        [TestCase(SessionConnectionState.Disconnected, false, false)]
        public void CloseUsesConnectionStateRatherThanWindowPresence(SessionConnectionState state, bool approve, bool cancelled)
        {
            using (var panel = new TestPanel(state))
            {
                panel.Approve = approve;
                Assert.AreEqual(cancelled, panel.RequestClose(CloseReason.UserClosing));
                Assert.AreEqual(state == SessionConnectionState.Disconnected ? 0 : 1, panel.PromptCount);
            }
        }

        [Test]
        public void CloseQuestionContainsOnlyTheTabName()
        {
            Assert.AreEqual("Close tab 'server'?", ctlPuttyPanel.GetCloseConfirmationMessage("server"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GuiLoadsMiddleClickPreferenceAndDoesNotApplyAnUnsavedChange(bool enabled)
        {
            bool original = SuperPutty.SuperPuTTY.Settings.MiddleClickClosesWithoutConfirmation;
            try
            {
                SuperPutty.SuperPuTTY.Settings.MiddleClickClosesWithoutConfirmation = enabled;
                using (var options = new dlgFindPutty())
                {
                    var checkbox = (CheckBox)options.Controls.Find("checkMiddleClickClose", true).Single();
                    Assert.AreEqual(enabled, checkbox.Checked);
                    Assert.AreEqual("Tabs and Docking", checkbox.Parent.Text);
                    Assert.IsTrue(checkbox.Parent.ClientRectangle.Contains(checkbox.Bounds));
                    checkbox.Checked = !enabled;
                    Assert.AreEqual(enabled, SuperPutty.SuperPuTTY.Settings.MiddleClickClosesWithoutConfirmation);
                }
            }
            finally { SuperPutty.SuperPuTTY.Settings.MiddleClickClosesWithoutConfirmation = original; }
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void ActiveTabHonorsCloseConfirmation(bool approve, bool cancelled)
        {
            using (TestPanel panel = new TestPanel(true))
            {
                panel.Approve = approve;
                Assert.AreEqual(cancelled, panel.RequestClose(CloseReason.UserClosing));
                Assert.AreEqual(1, panel.PromptCount);
            }
        }

        [Test]
        public void InactiveTabAndOwnerShutdownDoNotPrompt()
        {
            using (TestPanel panel = new TestPanel(false))
            {
                Assert.IsFalse(panel.RequestClose(CloseReason.UserClosing));
                Assert.AreEqual(0, panel.PromptCount);
            }
            using (TestPanel panel = new TestPanel(true))
            {
                Assert.IsFalse(panel.RequestClose(CloseReason.FormOwnerClosing));
                Assert.AreEqual(0, panel.PromptCount);
            }
        }

        [Test]
        public void AutomaticOrAlreadyConfirmedCloseDoesNotPrompt()
        {
            using (TestPanel panel = new TestPanel(true))
            {
                IntPtr handle = panel.Handle;
                panel.CloseWithoutConfirmation();
                Assert.AreEqual(0, panel.PromptCount);
                Assert.IsTrue(panel.IsDisposed);
            }
        }

        [Test]
        public void RestartShortcutCanBeConfiguredAndLoaded()
        {
            var settings = SuperPutty.SuperPuTTY.Settings;
            Keys original = settings.Action_RestartSession_Shortcut;
            try
            {
                settings.UpdateFromShortcuts(new[] { new KeyboardShortcut
                {
                    Name = "RestartSession", Key = Keys.R, Modifiers = Keys.Control | Keys.Shift
                }});
                KeyboardShortcut shortcut = settings.LoadShortcuts().Single(s => s.Name == "RestartSession");
                Assert.AreEqual(Keys.R, shortcut.Key);
                Assert.AreEqual(Keys.Control | Keys.Shift, shortcut.Modifiers);
            }
            finally { settings.Action_RestartSession_Shortcut = original; }
        }

        [TestCase(ConnectionProtocol.SSH, true)]
        [TestCase(ConnectionProtocol.Telnet, true)]
        [TestCase(ConnectionProtocol.VNC, false)]
        [TestCase(ConnectionProtocol.RDP, false)]
        [TestCase(ConnectionProtocol.PS, false)]
        [TestCase(ConnectionProtocol.WINCMD, false)]
        [TestCase(ConnectionProtocol.Mintty, false)]
        public void RestartCommandIsLimitedToPuttyClients(ConnectionProtocol protocol, bool expected)
        {
            Assert.AreEqual(expected, ctlPuttyPanel.SupportsPuttyRestart(protocol));
        }

        private sealed class TestPanel : ctlPuttyPanel
        {
            public bool Approve;
            public int PromptCount;

            public TestPanel(bool active)
                : base(new SessionData { Proto = ConnectionProtocol.SSH, Host = "test", SessionName = "Test" }, null)
            {
                AppPanel.ApplicationName = String.Empty;
                if (active)
                    typeof(ApplicationPanel).GetField("m_Process", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(AppPanel, Process.GetCurrentProcess());
            }

            public TestPanel(SessionConnectionState state) : this(false)
            {
                var original = AppPanel;
                Controls.Remove(original);
                original.Dispose();
                var replacement = new StatePanel(state);
                typeof(ctlPuttyPanel).GetProperty("AppPanel").SetValue(this, replacement, null);
                Controls.Add(replacement);
            }

            protected override bool ConfirmSessionClose()
            {
                PromptCount++;
                return Approve;
            }

            public bool RequestClose(CloseReason reason)
            {
                var args = new FormClosingEventArgs(reason, false);
                OnFormClosing(args);
                return args.Cancel;
            }
        }

        private sealed class StatePanel : ApplicationPanel
        {
            private readonly SessionConnectionState state;
            internal StatePanel(SessionConnectionState state) : base(ConnectionProtocol.SSH) { this.state = state; }
            public override bool ExternalProcessCaptured { get { return true; } }
            public override SessionConnectionState ConnectionState { get { return state; } }
        }
    }
}
