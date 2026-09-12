using NUnit.Framework;
using SuperPutty;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class AboutBoxTests
    {
        [Test]
        [Apartment(ApartmentState.STA)]
        public void AboutDialogDisplaysEditionLineageAndLicenseWithoutClipping()
        {
            using (var dialog = new AboutBox1())
            {
                Assert.AreEqual("About SuperPuTTY Community Edition", dialog.Text);
                Assert.AreEqual("SuperPuTTY Community Edition 1.7.5",
                    dialog.Controls.Find("labelProductName", true)[0].Text);
                Assert.AreEqual("Based on the original SuperPuTTY by Jim Radford",
                    dialog.Controls.Find("labelVersion", true)[0].Text);
                Assert.AreEqual("Community-maintained fork by Chris Thornton",
                    dialog.Controls.Find("labelMaintainer", true)[0].Text);
                string notice = dialog.Controls.Find("textBox1", true)[0].Text;
                StringAssert.Contains("This is not an official upstream SuperPuTTY release.", notice);
                StringAssert.Contains("Licensed under the MIT License.", notice);
                StringAssert.Contains("https://github.com/jimradford/superputty", notice);
                StringAssert.Contains("2026 Chris Thornton", notice);
                string license = dialog.Controls.Find("textBox2", true)[0].Text;
                StringAssert.Contains("Jim Radford", license);
                StringAssert.Contains("Permission is hereby granted", license);
                StringAssert.Contains("THE SOFTWARE IS PROVIDED", license);
                dialog.PerformLayout();
                foreach (Control control in dialog.Controls)
                {
                    if (control is Label)
                        Assert.LessOrEqual(control.Right, dialog.ClientSize.Width, control.Name);
                }
            }
        }

        [Test]
        public void ExecutableMetadataPreservesSettingsIdentityAndBothCopyrights()
        {
            var assembly = typeof(SuperPutty.SuperPuTTY).Assembly;
            Assert.AreEqual("SuperPutty.exe", Path.GetFileName(assembly.Location));
            Assert.AreEqual("SuperPuTTY", assembly.GetCustomAttribute<AssemblyProductAttribute>().Product);
            Assert.AreEqual("SuperPuTTY Community Edition", assembly.GetCustomAttribute<AssemblyTitleAttribute>().Title);
            Assert.AreEqual("Chris Thornton", assembly.GetCustomAttribute<AssemblyCompanyAttribute>().Company);
            var info = FileVersionInfo.GetVersionInfo(assembly.Location);
            Assert.AreEqual("SuperPuTTY Community Edition", info.FileDescription);
            StringAssert.Contains("2009 - 2023 Jim Radford", info.LegalCopyright);
            StringAssert.Contains("2026 Chris Thornton", info.LegalCopyright);
        }

        [Test]
        public void AboutDialogSeparatesOriginalReleaseFromCurrentUpdates()
        {
            Assert.AreEqual(
                "Version 1.5.0.0 Copyright (c) 2009 - 2023 Jim Radford",
                AboutBox1.OriginalReleaseAttribution);
            Assert.AreEqual(
                "https://www.jimradford.com",
                AboutBox1.OriginalAuthorUrl);
            Assert.AreEqual(
                "Community-maintained fork by Chris Thornton",
                AboutBox1.UpdateAttribution);
            Assert.AreEqual(
                "https://github.com/greyhair-atx/superputty",
                AboutBox1.CommunityRepositoryUrl);
        }
    }
}
