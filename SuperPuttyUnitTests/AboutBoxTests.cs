using NUnit.Framework;
using SuperPutty;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class AboutBoxTests
    {
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
                "Updates by C. Thornton",
                AboutBox1.UpdateAttribution);
            Assert.AreEqual(
                "https://github.com/greyhair-atx/superputty",
                AboutBox1.CommunityRepositoryUrl);
        }
    }
}
