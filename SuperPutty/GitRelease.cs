using System.Runtime.Serialization;
using System;
using System.Text.RegularExpressions;

namespace SuperPutty
{
    [DataContract]
    public class GitRelease
    {
        [DataMember(Name = "tag_name")]
        public string version;
        [DataMember(Name = "html_url")]
        public string release_url;

        public Version GetVersion()
        {
            Match match = Regex.Match(this.version ?? String.Empty, @"\d+(?:\.\d+){1,3}");
            Version parsedVersion;
            if (!match.Success || !Version.TryParse(match.Value, out parsedVersion))
            {
                throw new FormatException("The release tag does not contain a valid version: " + this.version);
            }

            return parsedVersion;
        }
    }
}
