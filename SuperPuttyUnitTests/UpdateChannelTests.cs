using System;
using NUnit.Framework;
using SuperPutty;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class UpdateChannelTests
    {
        [TestCase("1.5.0.0", "1.5.0.0")]
        [TestCase("sp-1.6.1", "1.6.1")]
        [TestCase("v2.0.1-beta", "2.0.1")]
        public void ReleaseTagsProduceComparableVersions(string tag, string expected)
        {
            Assert.AreEqual(new Version(expected), new GitRelease { version = tag }.GetVersion());
        }

        [Test]
        public void ChannelsUseTheirRespectiveRepositories()
        {
            StringAssert.Contains("jimradford/superputty", UpdateChannel.GetReleaseApiUrl(UpdateChannel.OfficialUpstream));
            StringAssert.Contains("greyhair-atx/superputty", UpdateChannel.GetReleaseApiUrl(UpdateChannel.CommunityFork));
        }

        [Test]
        public void UnknownChannelFallsBackToOfficialUpstream()
        {
            Assert.AreEqual(
                UpdateChannel.GetReleaseApiUrl(UpdateChannel.OfficialUpstream),
                UpdateChannel.GetReleaseApiUrl("old or corrupt setting"));
        }
    }
}
