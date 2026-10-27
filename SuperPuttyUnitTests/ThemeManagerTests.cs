using System.Drawing;
using System.Drawing.Imaging;
using NUnit.Framework;
using SuperPutty.Gui;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class ThemeManagerTests
    {
        [TestCase(null, ThemeManager.Light)]
        [TestCase("", ThemeManager.Light)]
        [TestCase("unrecognized", ThemeManager.Light)]
        [TestCase("light", ThemeManager.Light)]
        [TestCase("DARK", ThemeManager.Dark)]
        [TestCase("dark modern", ThemeManager.DarkModern)]
        [TestCase("system", ThemeManager.FollowSystem)]
        public void StoredThemeNamesAreCaseInsensitiveWithSafeFallback(string input, string expected)
        {
            Assert.AreEqual(expected, ThemeManager.Normalize(input));
        }

        [TestCase(ThemeManager.Dark, true)]
        [TestCase(ThemeManager.DarkModern, true)]
        [TestCase(ThemeManager.Light, false)]
        [TestCase("unknown", false)]
        public void ExplicitThemesDoNotDependOnTheWindowsPreference(string input, bool expected)
        {
            Assert.AreEqual(expected, ThemeManager.Resolve(input));
        }

        [Test]
        public void IconConversionPreservesEnclosedWhiteDetails()
        {
            using (var source = new Bitmap(5, 5, PixelFormat.Format24bppRgb))
            {
                using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.White);
                for (int x = 1; x <= 3; x++)
                    for (int y = 1; y <= 3; y++)
                        source.SetPixel(x, y, Color.Blue);
                source.SetPixel(2, 2, Color.White);
                using (var converted = (Bitmap)ThemeManager.WithTransparentBackground(source))
                {
                    Assert.AreEqual(0, converted.GetPixel(0, 0).A);
                    Assert.AreEqual(Color.White.ToArgb(), converted.GetPixel(2, 2).ToArgb());
                    Assert.AreEqual(Color.Blue.ToArgb(), converted.GetPixel(1, 1).ToArgb());
                    Assert.AreEqual(255, source.GetPixel(0, 0).A);
                }
            }
        }

        [Test]
        public void IconsWithAlphaArePreserved()
        {
            using (var source = new Bitmap(2, 2, PixelFormat.Format32bppArgb))
                Assert.AreSame(source, ThemeManager.WithTransparentBackground(source));
        }
    }
}
