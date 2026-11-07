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
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("InteractiveUiTest")]
    public class TabStripInteractionTests
    {
        [TestCase(false, 20)]
        [TestCase(true, 20)]
        [TestCase(false, 30)]
        [TestCase(true, 30)]
        [TestCase(false, 300)]
        [TestCase(true, 300)]
        public void OverflowHitTestingAndOutlinesDoNotThrow(bool modern, int count)
        {
            using (var fixture = new TabFixture(modern, count))
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

        [TestCase(false, 20)]
        [TestCase(true, 20)]
        [TestCase(false, 30)]
        [TestCase(true, 30)]
        [TestCase(false, 100)]
        [TestCase(true, 100)]
        public void CloseButtonsWorkWithoutAPriorHoverAfterOverflowAndResize(bool modern, int count)
        {
            using (var fixture = new TabFixture(modern, count))
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

        [TestCase(false)]
        [TestCase(true)]
        public void HundredsOfTabsRemainSelectableAndMoreCanOpenAfterClosing(bool modern)
        {
            using (var fixture = new TabFixture(modern, 300))
            {
                Assert.AreEqual(300, fixture.OpenDocumentCount);
                for (int index = 0; index < fixture.Documents.Count; index++)
                {
                    var document = fixture.Documents[index];
                    document.Activate();
                    fixture.Strip.PaintNow();
                    Assert.AreSame(document, document.Pane.ActiveContent);
                    var close = fixture.Strip.CloseBounds(index);
                    Assert.AreEqual(index, fixture.Strip.ProbeHit(new Point(close.Left - 5, close.Top + close.Height / 2)),
                        "Every overflow tab must become visible and selectable, including indexes above 255.");
                }
                for (int removed = 0; removed < 16; removed++)
                {
                    int last = fixture.Documents.Count - 1;
                    var document = fixture.Documents[last];
                    document.Close();
                    Assert.IsTrue(document.IsDisposed);
                    fixture.Documents.RemoveAt(last);
                }
                for (int added = 0; added < 32; added++) fixture.AddDocument();
                Assert.AreEqual(316, fixture.OpenDocumentCount);
                fixture.Documents[315].Activate();
                fixture.Strip.PaintNow();
                Assert.AreSame(fixture.Documents[315], fixture.Documents[0].Pane.ActiveContent);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OverflowListIncludesEveryTabAndReleasesItemsAndImages(bool modern)
        {
            using (var fixture = new TabFixture(modern, 300))
            {
                var strip = fixture.Strip;
                strip.PopulateWindowList();
                var menu = strip.WindowList;
                Assert.AreEqual(300, menu.Items.Count);
                for (int index = 0; index < menu.Items.Count; index++)
                {
                    Assert.AreSame(fixture.Documents[index], menu.Items[index].Tag);
                    menu.Items[index].PerformClick();
                    Assert.AreSame(fixture.Documents[index], fixture.Documents[0].Pane.ActiveContent);
                }
                var oldItem = menu.Items[0];
                var oldImage = oldItem.Image;
                int oldDisposed = 0;
                oldItem.Disposed += (sender, args) => oldDisposed++;
                Assert.IsNotNull(oldImage);
                strip.PopulateWindowList();
                Assert.AreEqual(300, menu.Items.Count);
                Assert.AreEqual(1, oldDisposed, "Rebuilding the list must dispose detached items.");
                Assert.Throws<ArgumentException>(() => ((Bitmap)oldImage).GetPixel(0, 0));
                var finalItem = menu.Items[0];
                var finalImage = finalItem.Image;
                int finalDisposed = 0;
                finalItem.Disposed += (sender, args) => finalDisposed++;
                strip.Dispose();
                Assert.AreEqual(1, finalDisposed);
                Assert.Throws<ArgumentException>(() => ((Bitmap)finalImage).GetPixel(0, 0));
            }
        }

        private sealed class TabFixture : IDisposable
        {
            private readonly Form host;
            private readonly DockPanel panel;
            private readonly ThemeBase theme;
            internal readonly List<DockContent> Documents = new List<DockContent>();
            internal TestStrip Strip { get { return (TestStrip)Documents[0].DockHandler.Pane.TabStripControl; } }
            internal int OpenDocumentCount { get { return panel.DocumentsCount; } }

            internal TabFixture(bool modern, int count)
            {
                theme = modern ? (ThemeBase)new DarkModernTheme() : new OutlinedVS2015LightTheme();
                theme.Extender.DockPaneStripFactory = new TestFactory();
                host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    Location = new Point(-16000, -16000), Size = new Size(420, 240) };
                panel = new DockPanel { Dock = DockStyle.Fill, DocumentStyle = DocumentStyle.DockingWindow, Theme = theme };
                host.Controls.Add(panel);
                host.Show();
                for (int i = 0; i < count; i++)
                    AddDocument();
                host.PerformLayout();
                Strip.Size = new Size(340, 28);
            }

            internal void AddDocument()
            {
                var document = new DockContent { Text = "Long session name for overflow " + Documents.Count };
                if (Documents.Count == 0) document.Show(panel, DockState.Document);
                else document.Show(Documents[0].Pane, null);
                Documents.Add(document);
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
            internal ContextMenuStrip WindowList { get { return (ContextMenuStrip)typeof(OutlinedVS2015DockPaneStrip)
                .GetProperty("SelectMenu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(this, null); } }
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
