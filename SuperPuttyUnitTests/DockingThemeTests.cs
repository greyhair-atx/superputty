using System.Drawing;
using NUnit.Framework;
using SuperPutty.Gui.Docking;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class DockingThemeTests
    {
        [Test]
        public void OutlinedThemeDefinesDistinctDocumentTabColors()
        {
            using (var theme = new OutlinedVS2015LightTheme())
            {
                Assert.That(theme.ColorPalette.TabSelectedActive.Background,
                    Is.EqualTo(Color.FromArgb(89, 89, 89)));
                Assert.That(theme.ColorPalette.TabSelectedActive.Text, Is.EqualTo(Color.White));
                Assert.That(theme.ColorPalette.TabSelectedInactive.Background,
                    Is.EqualTo(Color.FromArgb(89, 89, 89)));
                Assert.That(theme.ColorPalette.TabSelectedInactive.Text, Is.EqualTo(Color.White));
                Assert.That(theme.ColorPalette.TabUnselected.Background,
                    Is.EqualTo(Color.FromArgb(230, 230, 230)));
                Assert.That(theme.ColorPalette.TabUnselected.Text, Is.EqualTo(Color.Black));
                Assert.That(theme.ColorPalette.TabUnselectedHovered.Background,
                    Is.EqualTo(Color.FromArgb(160, 160, 160)));
                Assert.That(theme.ColorPalette.TabUnselectedHovered.Text, Is.EqualTo(Color.Black));
                Assert.That(OutlinedVS2015LightTheme.ActiveTabOutline, Is.EqualTo(Color.White));
                Assert.That(OutlinedVS2015LightTheme.InactiveTabOutline,
                    Is.EqualTo(Color.FromArgb(160, 160, 160)));
                Assert.That(OutlinedVS2015LightTheme.HoveredTabOutline, Is.EqualTo(Color.White));
                Assert.That(theme.Extender.DockPaneStripFactory,
                    Is.TypeOf<OutlinedVS2015DockPaneStripFactory>());
            }
        }

        [Test]
        public void DocumentTabsHaveSevenPixelRoundedTopCornersAndOpenFrameEdge()
        {
            Assert.That(OutlinedVS2015DockPaneStrip.DocumentTabCornerRadius, Is.EqualTo(7));

            using (var path = OutlinedVS2015DockPaneStrip.CreateDocumentTabPath(
                new Rectangle(0, 0, 100, 24), false))
            {
                Assert.That(path.IsVisible(1, 1), Is.False,
                    "The square top-left corner should be outside the rounded tab.");
                Assert.That(path.IsVisible(7, 1), Is.True,
                    "The tab should begin filling immediately after the seven-pixel corner.");
                Assert.That(path.IsVisible(50, 22), Is.True,
                    "The lower edge must remain square so it can join the document frame.");
            }
        }
    }
}
