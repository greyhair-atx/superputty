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
                    Is.EqualTo(Color.FromArgb(230, 230, 230)));
                Assert.That(theme.ColorPalette.TabSelectedActive.Text, Is.EqualTo(Color.Black));
                Assert.That(theme.ColorPalette.TabUnselected.Background,
                    Is.EqualTo(Color.FromArgb(89, 89, 89)));
                Assert.That(theme.ColorPalette.TabUnselected.Text, Is.EqualTo(Color.White));
                Assert.That(theme.Extender.DockPaneStripFactory,
                    Is.TypeOf<OutlinedVS2015DockPaneStripFactory>());
            }
        }
    }
}
