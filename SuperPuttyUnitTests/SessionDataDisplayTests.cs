using System.IO;
using System.Xml.Serialization;
using NUnit.Framework;
using SuperPutty.Data;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class SessionDataDisplayTests
    {
        [TestCase(null, "ssh://server.example.com:22")]
        [TestCase("", "ssh://server.example.com:22")]
        [TestCase("alice", "ssh://alice@server.example.com:22")]
        public void DisplayUriHandlesMissingEmptyAndPopulatedUsername(string username, string expected)
        {
            var session = new SessionData
            {
                Proto = ConnectionProtocol.SSH,
                Host = "server.example.com",
                Port = 22,
                Username = username
            };

            Assert.AreEqual(expected, session.ToString());
        }

        [Test]
        public void LegacySessionWithoutUsernameAttributeCanBeDisplayed()
        {
            const string xml = "<SessionData SessionId=\"Lab/Server\" SessionName=\"Server\" Host=\"server.example.com\" Port=\"22\" Proto=\"SSH\" PuttySession=\"Default Settings\" />";
            var serializer = new XmlSerializer(typeof(SessionData));
            using (var reader = new StringReader(xml))
            {
                var session = (SessionData)serializer.Deserialize(reader);

                Assert.IsNull(session.Username);
                Assert.AreEqual("ssh://server.example.com:22", session.ToString());
            }
        }
    }
}
