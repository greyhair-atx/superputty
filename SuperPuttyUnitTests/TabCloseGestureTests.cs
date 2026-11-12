using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Gui.Docking;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("InteractiveUiTest")]
    public class TabCloseGestureTests
    {
        private bool original;
        [SetUp] public void SetUp()
        {
            original = SuperPutty.SuperPuTTY.Settings.MiddleClickClosesWithoutConfirmation;
            Point previous = Cursor.Position;
            Point probe = new Point(Screen.PrimaryScreen.WorkingArea.Left + 80, Screen.PrimaryScreen.WorkingArea.Top + 80);
            try
            {
                Cursor.Position = probe;
                if (Cursor.Position != probe)
                    Assert.Ignore("Tab gestures require an interactive desktop that allows cursor movement.");
            }
            finally { Cursor.Position = previous; }
        }
        [TearDown] public void TearDown() { SuperPutty.SuperPuTTY.Settings.MiddleClickClosesWithoutConfirmation = original; }

        [Test, Combinatorial]
        public void MiddleClickHonorsTheSettingAndTargetsTheHoveredTab(
            [Values("Light", "Dark", "Dark Modern")] string theme,
            [Values(false, true)] bool enabled,
            [Values(SessionConnectionState.Connected, SessionConnectionState.Unknown)] SessionConnectionState state)
        {
            SuperPutty.SuperPuTTY.Settings.MiddleClickClosesWithoutConfirmation = enabled;
            using (var fixture = new Fixture(theme, state))
            {
                fixture.MiddleClickFirst();
                Assert.AreEqual(enabled, fixture.Target.IsDisposed);
                Assert.AreEqual(enabled ? 0 : 1, fixture.Target.PromptCount);
                Assert.IsFalse(fixture.Other.IsDisposed, "Middle click must close the hovered tab rather than the active tab.");
                Assert.AreEqual(0, fixture.Other.PromptCount);
            }
        }

        [TestCase("Light")]
        [TestCase("Dark")]
        [TestCase("Dark Modern")]
        public void XStillPromptsAfterAnUnconfirmedMiddleClick(string theme)
        {
            SuperPutty.SuperPuTTY.Settings.MiddleClickClosesWithoutConfirmation = true;
            using (var fixture = new Fixture(theme, SessionConnectionState.Unknown))
            {
                fixture.MiddleClickFirst();
                Assert.IsTrue(fixture.Target.IsDisposed);
                fixture.ClickOtherX();
                Assert.AreEqual(1, fixture.Other.PromptCount);
                Assert.IsFalse(fixture.Other.IsDisposed, "Canceling the X confirmation must retain the tab.");
            }
        }

        [TestCase("Light")]
        [TestCase("Dark")]
        [TestCase("Dark Modern")]
        public void CloseRestrictionsAndEmptyStripClicksDoNotCloseTabs(string theme)
        {
            SuperPutty.SuperPuTTY.Settings.MiddleClickClosesWithoutConfirmation = true;
            using (var fixture = new Fixture(theme, SessionConnectionState.Connected))
            {
                fixture.Target.CloseButton = false;
                fixture.MiddleClickFirst();
                Assert.IsFalse(fixture.Target.IsDisposed, "The setting must respect docking close restrictions.");
                Assert.IsFalse(fixture.Other.IsDisposed);
                fixture.MiddleClickGap();
                Assert.IsFalse(fixture.Target.IsDisposed);
                Assert.IsFalse(fixture.Other.IsDisposed);
                fixture.ClickOtherX();
                Assert.AreEqual(1, fixture.Other.PromptCount, "A rejected/empty middle click must not leave confirmation disabled.");
            }
        }

        [TestCase("Light")]
        [TestCase("Dark")]
        [TestCase("Dark Modern")]
        public void ClosingAnotherTabFromAClosingHandlerStillRequiresConfirmation(string theme)
        {
            SuperPutty.SuperPuTTY.Settings.MiddleClickClosesWithoutConfirmation = true;
            using (var fixture = new Fixture(theme, SessionConnectionState.Unknown))
            {
                fixture.Target.FormClosing += (sender, args) => fixture.Other.Close();
                fixture.MiddleClickFirst();
                Assert.IsTrue(fixture.Target.IsDisposed);
                Assert.AreEqual(0, fixture.Target.PromptCount);
                Assert.AreEqual(1, fixture.Other.PromptCount);
                Assert.IsFalse(fixture.Other.IsDisposed, "Only the middle-clicked tab may bypass confirmation.");
            }
        }

        [TestCase("Light")]
        [TestCase("Dark")]
        [TestCase("Dark Modern")]
        public void DockedSessionTabsAlsoHonorUnconfirmedMiddleClick(string theme)
        {
            SuperPutty.SuperPuTTY.Settings.MiddleClickClosesWithoutConfirmation = true;
            using (var fixture = new Fixture(theme, SessionConnectionState.Unknown, DockState.DockRight))
            {
                fixture.MiddleClickFirst();
                Assert.IsTrue(fixture.Target.IsDisposed);
                Assert.AreEqual(0, fixture.Target.PromptCount);
                Assert.IsFalse(fixture.Other.IsDisposed);
            }
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        private sealed class Fixture : IDisposable
        {
            private readonly Form host;
            private readonly DockPanel panel;
            private readonly ThemeBase theme;
            private readonly TabCloseMouseFilter filter;
            private readonly Point originalCursor;
            internal readonly SessionPanel Target;
            internal readonly SessionPanel Other;
            private DockPaneStripBase Strip { get { return Other.Pane.TabStripControl; } }
            private const BindingFlags Methods = BindingFlags.Instance | BindingFlags.NonPublic;

            internal Fixture(string name, SessionConnectionState state, DockState dockState = DockState.Document)
            {
                originalCursor = Cursor.Position;
                theme = name == "Light" ? (ThemeBase)new OutlinedVS2015LightTheme()
                    : name == "Dark" ? (ThemeBase)new VS2015DarkTheme() : new DarkModernTheme();
                host = new Form { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual,
                    Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 40, Screen.PrimaryScreen.WorkingArea.Top + 40),
                    Size = new Size(640, 320) };
                panel = new DockPanel { Dock = DockStyle.Fill, Theme = theme, DocumentStyle = DocumentStyle.DockingWindow };
                host.Controls.Add(panel);
                filter = new TabCloseMouseFilter(panel);
                host.Show();
                Target = new SessionPanel(state) { Text = "Target" };
                Other = new SessionPanel(state) { Text = "Other" };
                Target.Show(panel, dockState);
                Other.Show(Target.Pane, null);
                Other.Activate();
                Paint();
            }

            private void Paint()
            {
                host.PerformLayout();
                using (var bitmap = new Bitmap(Math.Max(1, Strip.Width), Math.Max(1, Strip.Height)))
                using (var graphics = Graphics.FromImage(bitmap))
                    Strip.GetType().GetMethod("OnPaint", Methods).Invoke(Strip, new object[] { new PaintEventArgs(graphics, Strip.ClientRectangle) });
            }

            private void MiddleClick(Point location)
            {
                Cursor.Position = Strip.PointToScreen(location);
                var message = Message.Create(Strip.Handle, TabCloseMouseFilter.MiddleButtonDown, new IntPtr(0x10),
                    new IntPtr((location.Y << 16) | (location.X & 0xffff)));
                if (!filter.PreFilterMessage(ref message)) SendMessage(message.HWnd, message.Msg, message.WParam, message.LParam);
            }

            internal void MiddleClickFirst()
            {
                Paint();
                Rectangle bounds = Strip.AccessibilityObject.GetChild(0).Bounds;
                MiddleClick(Strip.PointToClient(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)));
            }

            internal void MiddleClickGap()
            {
                Paint();
                Point point = new Point(Strip.Width - 60, Strip.Height / 2);
                Assert.AreEqual(-1, Strip.GetType().GetMethod("HitTest", Methods, null, new[] { typeof(Point) }, null).Invoke(Strip, new object[] { point }));
                MiddleClick(point);
            }

            internal void ClickOtherX()
            {
                Other.Activate();
                Paint();
                int index = Other.Pane.DisplayingContents.IndexOf(Other);
                var tab = Strip.GetType().GetMethod("GetTabRectangle", Methods).Invoke(Strip, new object[] { index });
                var close = (Rectangle)Strip.GetType().GetMethod("GetCloseButtonRect", Methods).Invoke(Strip, new[] { tab });
                Point point = new Point(close.Left + close.Width / 2, close.Top + close.Height / 2);
                Cursor.Position = Strip.PointToScreen(point);
                Strip.GetType().GetMethod("OnMouseMove", Methods).Invoke(Strip, new object[] { new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0) });
                Strip.GetType().GetMethod("OnMouseClick", Methods).Invoke(Strip, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
            }

            public void Dispose()
            {
                filter.Dispose();
                Target.Dispose();
                Other.Dispose();
                host.Dispose();
                theme.Dispose();
                Cursor.Position = originalCursor;
            }
        }

        private sealed class SessionPanel : ctlPuttyPanel
        {
            internal int PromptCount;
            internal SessionPanel(SessionConnectionState state)
                : base(new SessionData { Proto = ConnectionProtocol.SSH, Host = "test", SessionName = "Test" }, null)
            {
                var old = AppPanel;
                Controls.Remove(old);
                old.Dispose();
                var replacement = new StatePanel(state);
                typeof(ctlPuttyPanel).GetProperty("AppPanel").SetValue(this, replacement, null);
                Controls.Add(replacement);
            }
            protected override void OnLoad(EventArgs e) { } // No external client in these input-routing tests.
            protected override bool ConfirmSessionClose() { PromptCount++; return false; }
        }

        private sealed class StatePanel : ApplicationPanel
        {
            private readonly SessionConnectionState state;
            internal StatePanel(SessionConnectionState state) : base(ConnectionProtocol.SSH) { this.state = state; }
            public override SessionConnectionState ConnectionState { get { return state; } }
        }
    }
}
