using System;

namespace SuperPutty
{
    public static class UpdateChannel
    {
        public const string OfficialUpstream = "Official upstream";
        public const string CommunityFork = "Community fork";

        public static readonly string[] Names = { OfficialUpstream, CommunityFork };

        public static string GetReleaseApiUrl(string channel)
        {
            return String.Equals(channel, CommunityFork, StringComparison.Ordinal)
                ? "https://api.github.com/repos/greyhair-atx/superputty/releases/latest"
                : "https://api.github.com/repos/jimradford/superputty/releases/latest";
        }
    }
}
