using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Gui.Docking;
using SuperPutty.Scp;
using SuperPutty.Utils;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class WindowLocationDiagnosticsTests
    {
        private Logger logger;
        private Level originalLevel;
        private Level originalThreshold;
        private bool originalConfigured;
        private MemoryAppender appender;

        [SetUp]
        public void CaptureDiagnosticMessages()
        {
            logger = (Logger)LogManager.GetLogger(typeof(frmSuperPutty).Assembly, typeof(frmSuperPutty)).Logger;
            originalLevel = logger.Level;
            originalThreshold = logger.Hierarchy.Threshold;
            originalConfigured = logger.Hierarchy.Configured;
            appender = new MemoryAppender();
            appender.ActivateOptions();
            logger.AddAppender(appender);
            logger.Level = Level.Info;
            logger.Hierarchy.Threshold = Level.All;
            logger.Hierarchy.Configured = true;
        }

        [TearDown]
        public void RestoreLogging()
        {
            logger.RemoveAppender(appender);
            logger.Level = originalLevel;
            logger.Hierarchy.Threshold = originalThreshold;
            logger.Hierarchy.Configured = originalConfigured;
            appender.Close();
        }

        private string[] Messages()
        {
            return appender.GetEvents().Select(e => e.RenderedMessage)
                .Where(message => message.Contains("WindowLocations:")).ToArray();
        }

        [Test]
        public void MixedScpTerminalAndManagedDocumentsAreAllLogged()
        {
            using (var fixture = new DockFixture())
            using (var scp = new PscpBrowserPanel { Text = "SCP diagnostic" })
            using (var terminal = NewTerminal())
            using (var managed = new DockContent { Text = "Managed diagnostic" })
            {
                scp.Show(fixture.Panel, DockState.Document);
                terminal.Show(fixture.Panel, DockState.Document);
                managed.Show(fixture.Panel, DockState.Document);
                scp.Activate();
                Assert.DoesNotThrow(() => frmSuperPutty.LogWindowLocations(fixture.Panel));
                string[] messages = Messages();
                Assert.AreEqual(3, messages.Length, "A non-terminal tab must not prevent later tabs from being logged.");
                Assert.That(messages.Single(message => message.Contains("SCP diagnostic")),
                    Does.Contain("SCP diagnostic*").And.Contain("type=PscpBrowserPanel").And.Contain("app=unavailable"));
                Assert.That(messages.Single(message => message.Contains("Terminal diagnostic")), Does.Contain("type=ctlPuttyPanel"));
                Assert.That(messages.Single(message => message.Contains("Managed diagnostic")), Does.Contain("type=DockContent"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrDisposedApplicationStillLogsTheDocument(bool disposeApplication)
        {
            using (var fixture = new DockFixture())
            using (var terminal = NewTerminal())
            {
                terminal.Show(fixture.Panel, DockState.Document);
                if (disposeApplication) terminal.AppPanel.Dispose();
                else typeof(ctlPuttyPanel).GetProperty("AppPanel").SetValue(terminal, null, null);
                Assert.DoesNotThrow(() => frmSuperPutty.LogWindowLocations(fixture.Panel));
                Assert.That(Messages().Single(), Does.Contain("Terminal diagnostic").And.Contain("app=unavailable"));
            }
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        public void MissingOrStaleNativeHandleDoesNotReportZeroBounds(long window)
        {
            using (var fixture = new DockFixture())
            using (var terminal = NewTerminal())
            {
                var original = terminal.AppPanel;
                terminal.Controls.Remove(original);
                original.Dispose();
                var replacement = new TestApplication { Window = new IntPtr(window) };
                typeof(ctlPuttyPanel).GetProperty("AppPanel").SetValue(terminal, replacement, null);
                terminal.Controls.Add(replacement);
                terminal.Show(fixture.Panel, DockState.Document);
                Assert.DoesNotThrow(() => frmSuperPutty.LogWindowLocations(fixture.Panel));
                Assert.That(Messages().Single(), Does.Contain("app=unavailable"));
            }
        }

        [Test]
        public void LiveNativeWindowBoundsAreLogged()
        {
            using (var fixture = new DockFixture())
            using (var nativeWindow = new Form { Location = new Point(123, 234), Size = new Size(345, 456) })
            using (var terminal = NewTerminal())
            {
                var original = terminal.AppPanel;
                terminal.Controls.Remove(original);
                original.Dispose();
                var replacement = new TestApplication { Window = nativeWindow.Handle };
                typeof(ctlPuttyPanel).GetProperty("AppPanel").SetValue(terminal, replacement, null);
                terminal.Controls.Add(replacement);
                terminal.Show(fixture.Panel, DockState.Document);
                var expected = new NativeMethods.RECT();
                Assert.IsTrue(NativeMethods.GetWindowRect(nativeWindow.Handle, ref expected));
                frmSuperPutty.LogWindowLocations(fixture.Panel);
                Assert.That(Messages().Single(), Does.Contain("app=" + expected).And.Contain(nativeWindow.Handle.ToString()));
            }
        }

        [Test]
        public void ClosedAndDetachedDocumentsDoNotAffectRemainingTabs()
        {
            using (var fixture = new DockFixture())
            using (var closed = new PscpBrowserPanel { Text = "Closed SCP" })
            using (var detached = new PscpBrowserPanel { Text = "Detached SCP" })
            using (var remaining = new PscpBrowserPanel { Text = "Remaining SCP" })
            {
                closed.Show(fixture.Panel, DockState.Document);
                detached.Show(fixture.Panel, DockState.Document);
                remaining.Show(fixture.Panel, DockState.Document);
                closed.Dispose();
                detached.DockPanel = null;
                frmSuperPutty.LogWindowLocations(fixture.Panel);
                Assert.That(Messages().Single(), Does.Contain("Remaining SCP"));
            }
        }

        [Test]
        public void NullEmptyAndDisposedDockPanelsAreSafe()
        {
            Assert.DoesNotThrow(() => frmSuperPutty.LogWindowLocations(null));
            using (var fixture = new DockFixture())
            {
                frmSuperPutty.LogWindowLocations(fixture.Panel);
                fixture.Panel.Dispose();
                Assert.DoesNotThrow(() => frmSuperPutty.LogWindowLocations(fixture.Panel));
                Assert.IsEmpty(Messages());
            }
        }

        private static ctlPuttyPanel NewTerminal()
        {
            var panel = new ctlPuttyPanel(new SessionData
                { Proto = ConnectionProtocol.SSH, Host = "unused", SessionName = "Terminal diagnostic" }, null);
            panel.AppPanel.ApplicationName = String.Empty;
            return panel;
        }

        private sealed class TestApplication : ApplicationPanel
        {
            internal IntPtr Window;
            internal TestApplication() : base(ConnectionProtocol.SSH) { }
            public override IntPtr AppWindowHandle { get { return Window; } }
        }

        private sealed class DockFixture : IDisposable
        {
            private readonly Form host;
            private readonly ThemeBase theme;
            internal readonly DockPanel Panel;

            internal DockFixture()
            {
                theme = new OutlinedVS2015LightTheme();
                host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    Location = new Point(-16000, -16000), Size = new Size(800, 600) };
                Panel = new DockPanel { Dock = DockStyle.Fill, DocumentStyle = DocumentStyle.DockingWindow, Theme = theme };
                host.Controls.Add(Panel);
                host.Show();
            }

            public void Dispose()
            {
                host.Dispose();
                theme.Dispose();
            }
        }
    }
}
