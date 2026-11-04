using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty.Gui.Docking;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class TabStripInteractionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void OverflowHitTestingAndOutlinesDoNotThrow(bool modern)
        {
            using (var fixture = new TabFixture(modern))
            {
                var strip = fixture.Strip;
                strip.PaintNow();
                Rectangle area = strip.TabArea;
                Assert.Greater(area.Width, 0);
                Assert.IsTrue(fixture.Documents.Count > 10);
                // These points include gaps and rounded corners, where no visible
                // tab matches and hit testing must traverse the hidden overflow.
                foreach (int y in new[] { area.Top, area.Top + area.Height / 2 })
                    for (int x = area.Left; x < area.Right; x += 7)
                    {
                        Assert.DoesNotThrow(() => strip.ProbeHit(new Point(x, y)));
                        Assert.DoesNotThrow(() => strip.MoveAt(new Point(x, y)));
                    }
                for (int index = 0; index < fixture.Documents.Count; index++)
                    using (var outline = strip.GetOutline(index)) { }
                using (var outline = strip.GetOutline(-1)) Assert.AreEqual(0, outline.PointCount);
                using (var outline = strip.GetOutline(fixture.Documents.Count)) Assert.AreEqual(0, outline.PointCount);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CloseButtonsWorkWithoutAPriorHoverAfterOverflowAndResize(bool modern)
        {
            using (var fixture = new TabFixture(modern))
            {
                for (int remaining = fixture.Documents.Count; remaining > 0; remaining--)
                {
                    var content = fixture.Documents[remaining - 1];
                    content.Activate();
                    var strip = fixture.Strip;
                    strip.Width = remaining % 2 == 0 ? 340 : 570;
                    strip.PaintNow();
                    Rectangle close = strip.CloseBounds(remaining - 1);
                    Assert.IsFalse(close.IsEmpty);
                    // No mouse movement updates the cached close-button hover.
                    strip.ClickAt(new Point(close.Left + close.Width / 2, close.Top + close.Height / 2));
                    Assert.IsTrue(content.IsDisposed, "Close must not depend on cached hover state.");
                }
            }
        }

        private sealed class TabFixture : IDisposable
        {
            private readonly Form host;
            private readonly DockPanel panel;
            private readonly ThemeBase theme;
            internal readonly List<DockContent> Documents = new List<DockContent>();
            internal TestStrip Strip { get { return (TestStrip)Documents[0].DockHandler.Pane.TabStripControl; } }

            internal TabFixture(bool modern)
            {
                theme = modern ? (ThemeBase)new DarkModernTheme() : new OutlinedVS2015LightTheme();
                theme.Extender.DockPaneStripFactory = new TestFactory();
                host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    Location = new Point(-16000, -16000), Size = new Size(420, 240) };
                panel = new DockPanel { Dock = DockStyle.Fill, DocumentStyle = DocumentStyle.DockingWindow, Theme = theme };
                host.Controls.Add(panel);
                host.Show();
                for (int i = 0; i < 20; i++)
                {
                    var document = new DockContent { Text = "Long session name for overflow " + i };
                    Documents.Add(document);
                    if (i == 0) document.Show(panel, DockState.Document);
                    else document.Show(Documents[0].Pane, null);
                }
                host.PerformLayout();
                Strip.Size = new Size(340, 28);
            }

            public void Dispose()
            {
                foreach (var document in Documents) document.Dispose();
                host.Dispose();
                theme.Dispose();
            }
        }

        private sealed class TestFactory : DockPanelExtender.IDockPaneStripFactory
        {
            public DockPaneStripBase CreateDockPaneStrip(DockPane pane) { return new TestStrip(pane); }
        }

        private sealed class TestStrip : OutlinedVS2015DockPaneStrip
        {
            internal TestStrip(DockPane pane) : base(pane) { }
            internal Rectangle TabArea { get { return (Rectangle)typeof(OutlinedVS2015DockPaneStrip)
                .GetProperty("TabsRectangle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(this, null); } }
            internal int ProbeHit(Point point) { return base.HitTest(point); }
            internal void MoveAt(Point point) { base.OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0)); }
            internal void ClickAt(Point point) { base.OnMouseClick(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)); }
            internal Rectangle CloseBounds(int index)
            {
                var type = typeof(OutlinedVS2015DockPaneStrip);
                var tab = type.GetMethod("GetTabRectangle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(this, new object[] { index });
                return (Rectangle)type.GetMethod("GetCloseButtonRect", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(this, new[] { tab });
            }
            internal void PaintNow()
            {
                using (var bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height)))
                using (var graphics = Graphics.FromImage(bitmap))
                    base.OnPaint(new PaintEventArgs(graphics, ClientRectangle));
            }
        }
    }
}
