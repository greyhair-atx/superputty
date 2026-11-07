using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class PuttyWindowFrameTests
    {
        private const uint FrameStyles = NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME;
        private const uint FrameExtendedStyles = NativeMethods.WS_EX_DLGMODALFRAME |
            NativeMethods.WS_EX_WINDOWEDGE | NativeMethods.WS_EX_CLIENTEDGE | NativeMethods.WS_EX_STATICEDGE;

        private sealed class TerminalWindow : NativeWindow, IDisposable
        {
            public TerminalWindow(bool scrollbars)
            {
                CreateHandle(new CreateParams
                {
                    Caption = "Frame regression fixture",
                    Style = unchecked((int)(NativeMethods.WS_POPUP | FrameStyles |
                        (scrollbars ? NativeMethods.WS_HSCROLL | NativeMethods.WS_VSCROLL : 0))),
                    ExStyle = unchecked((int)(FrameExtendedStyles | NativeMethods.WS_EX_TOOLWINDOW |
                        NativeMethods.WS_EX_NOACTIVATE)),
                    Width = 320,
                    Height = 240
                });
            }

            public void Dispose() { DestroyHandle(); }
        }

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr window, out NativeMethods.RECT rectangle);

        [TestCase(false), TestCase(true)]
        public void RemovingFramePreservesActivationStylesParentAndScrollbars(bool scrollbars)
        {
            using (var host = new Panel())
            using (var terminal = new TerminalWindow(scrollbars))
            {
                ApplicationPanel.ReparentWindow(terminal.Handle, host.Handle, true);
                uint original = unchecked((uint)NativeMethods.GetWindowLong(terminal.Handle, NativeMethods.GWL_STYLE));
                uint originalExtended = unchecked((uint)NativeMethods.GetWindowLong(terminal.Handle, NativeMethods.GWL_EXSTYLE));
                Assert.IsTrue(ApplicationPanel.RemovePuttyWindowFrame(terminal.Handle));
                Assert.AreEqual(original & ~FrameStyles,
                    unchecked((uint)NativeMethods.GetWindowLong(terminal.Handle, NativeMethods.GWL_STYLE)));
                Assert.AreEqual(originalExtended & ~FrameExtendedStyles,
                    unchecked((uint)NativeMethods.GetWindowLong(terminal.Handle, NativeMethods.GWL_EXSTYLE)));
                Assert.AreEqual(host.Handle, NativeMethods.GetAncestor(terminal.Handle, 1));

                var outer = new NativeMethods.RECT();
                Assert.IsTrue(NativeMethods.GetWindowRect(terminal.Handle, ref outer));
                NativeMethods.RECT client;
                Assert.IsTrue(GetClientRect(terminal.Handle, out client));
                Assert.AreEqual(scrollbars ? SystemInformation.VerticalScrollBarWidth : 0,
                    outer.Right - outer.Left - client.Right);
                Assert.AreEqual(scrollbars ? SystemInformation.HorizontalScrollBarHeight : 0,
                    outer.Bottom - outer.Top - client.Bottom);
                Assert.IsFalse(ApplicationPanel.RemovePuttyWindowFrame(terminal.Handle), "An unchanged frame needs no recalculation.");
            }
            Assert.IsFalse(ApplicationPanel.RemovePuttyWindowFrame(IntPtr.Zero));
        }

        [TestCase(ConnectionProtocol.SSH)]
        [TestCase(ConnectionProtocol.Raw)]
        [TestCase(ConnectionProtocol.Serial)]
        public void RefreshAndResizeKeepTwoPixelBoundsAndRemoveRestoredFrames(ConnectionProtocol protocol)
        {
            using (var panel = new ApplicationPanel(protocol))
            using (var terminal = new TerminalWindow(true))
            {
                panel.Size = new Size(640, 480);
                ApplicationPanel.ReparentWindow(terminal.Handle, panel.Handle, true);
                FieldInfo handle = typeof(ApplicationPanel).GetField("m_AppWin", BindingFlags.Instance | BindingFlags.NonPublic);
                handle.SetValue(panel, terminal.Handle);
                try
                {
                    panel.RefreshAppWindow();
                    AssertEmbeddedBounds(panel, terminal.Handle, new Rectangle(2, 2, 636, 476));

                    // PuTTY reapplies resize/sunken-edge styles from Change Settings.
                    int style = NativeMethods.GetWindowLong(terminal.Handle, NativeMethods.GWL_STYLE);
                    NativeMethods.SetWindowLong(terminal.Handle, NativeMethods.GWL_STYLE, style | (int)FrameStyles);
                    NativeMethods.SetWindowLong(terminal.Handle, NativeMethods.GWL_EXSTYLE, (int)FrameExtendedStyles);
                    panel.Size = new Size(800, 600);
                    AssertEmbeddedBounds(panel, terminal.Handle, new Rectangle(2, 2, 796, 596));
                    Assert.AreEqual(0, unchecked((uint)NativeMethods.GetWindowLong(terminal.Handle, NativeMethods.GWL_STYLE)) & FrameStyles);
                    Assert.AreEqual(0, unchecked((uint)NativeMethods.GetWindowLong(terminal.Handle, NativeMethods.GWL_EXSTYLE)) & FrameExtendedStyles);

                    NativeMethods.MoveWindow(terminal.Handle, 0, 0, 320, 240, false);
                    MethodInfo locationChanged = typeof(ApplicationPanel).GetMethod("WinEventProc", BindingFlags.Instance | BindingFlags.NonPublic);
                    locationChanged.Invoke(panel, new object[] { IntPtr.Zero,
                        (uint)NativeMethods.WinEvents.EVENT_OBJECT_LOCATIONCHANGE, terminal.Handle, -8 /* OBJID_CARET */, 0, 0u, 0u });
                    var unchanged = new NativeMethods.RECT();
                    NativeMethods.GetWindowRect(terminal.Handle, ref unchanged);
                    Assert.AreEqual(320, unchanged.Right - unchanged.Left, "Caret events must not resize the terminal.");
                    locationChanged.Invoke(panel, new object[] { IntPtr.Zero,
                        (uint)NativeMethods.WinEvents.EVENT_OBJECT_LOCATIONCHANGE, terminal.Handle, 0, 0, 0u, 0u });
                    AssertEmbeddedBounds(panel, terminal.Handle, new Rectangle(2, 2, 796, 596));
                }
                finally { handle.SetValue(panel, IntPtr.Zero); }
            }
        }

        private static void AssertEmbeddedBounds(ApplicationPanel panel, IntPtr window, Rectangle expected)
        {
            Assert.AreEqual(expected, panel.HostedWindowBounds);
            var outer = new NativeMethods.RECT();
            Assert.IsTrue(NativeMethods.GetWindowRect(window, ref outer));
            Point origin = panel.PointToScreen(Point.Empty);
            Assert.AreEqual(new Rectangle(origin.X + expected.X, origin.Y + expected.Y, expected.Width, expected.Height),
                Rectangle.FromLTRB(outer.Left, outer.Top, outer.Right, outer.Bottom));
        }

        [TestCase(0, 0), TestCase(1, 3), TestCase(3, 1), TestCase(4, 4), TestCase(5, 5)]
        public void SmallPanelsNeverProduceNegativeOrOutOfBoundsContent(int width, int height)
        {
            using (var panel = new ApplicationPanel(ConnectionProtocol.SSH))
            {
                panel.Size = new Size(width, height);
                Rectangle bounds = panel.HostedWindowBounds;
                Assert.GreaterOrEqual(bounds.Width, 0);
                Assert.GreaterOrEqual(bounds.Height, 0);
                Assert.GreaterOrEqual(bounds.Left, 0);
                Assert.GreaterOrEqual(bounds.Top, 0);
                Assert.LessOrEqual(bounds.Right, width);
                Assert.LessOrEqual(bounds.Bottom, height);
            }
        }

        [TestCase(ConnectionProtocol.RDP)]
        [TestCase(ConnectionProtocol.VNC)]
        [TestCase(ConnectionProtocol.Mintty)]
        [TestCase(ConnectionProtocol.PS)]
        [TestCase(ConnectionProtocol.WINCMD)]
        [TestCase(ConnectionProtocol.SCP)]
        public void OtherClientHostsKeepTheirFullClientArea(ConnectionProtocol protocol)
        {
            using (var panel = new ApplicationPanel(protocol))
            {
                panel.Size = new Size(640, 480);
                Assert.AreEqual(panel.ClientRectangle, panel.HostedWindowBounds);
            }
        }

        [Test]
        public void BorderPaintIsExactlyTwoPixelsEvenWhenTheThemeChangesTheBackground()
        {
            using (var panel = new ApplicationPanel(ConnectionProtocol.SSH))
            using (var image = new Bitmap(20, 20))
            {
                panel.Size = image.Size;
                panel.BackColor = Color.White;
                panel.DrawToBitmap(image, panel.ClientRectangle);
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                        Assert.AreEqual((x < 2 || y < 2 || x >= 18 || y >= 18 ? Color.FromArgb(96, 96, 96) : Color.White).ToArgb(),
                            image.GetPixel(x, y).ToArgb(), "Pixel {0},{1}", x, y);
            }
        }
    }
}
