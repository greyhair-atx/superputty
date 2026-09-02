using NUnit.Framework;
using SuperPutty;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class AboutBoxTests
    {
        [Test]
        public void AboutDialogCreditsCommunityUpdatesAndRepository()
        {
            Assert.AreEqual(
                "Updates by C. Thornton at https://github.com/greyhair-atx/superputty",
                AboutBox1.UpdateAttribution);
            Assert.AreEqual(
                "https://github.com/greyhair-atx/superputty",
                AboutBox1.CommunityRepositoryUrl);
        }
    }
}
