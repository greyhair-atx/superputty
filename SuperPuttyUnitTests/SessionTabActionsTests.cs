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
    }
}
