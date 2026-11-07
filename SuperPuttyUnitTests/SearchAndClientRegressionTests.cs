using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class SearchAndClientRegressionTests
    {
        [TestCase("CaseSensitive")]
        [TestCase("CaseInSensitive")]
        [TestCase("Regex")]
        public void ProfileOnlySessionSearchHandlesMissingHostname(string mode)
        {
            var session = new SessionData { SessionName = "ProfileOnly", PuttySession = "SavedProfile" };
            Assert.IsFalse(new SessionTreeview.SearchFilter(mode, "no-match").IsMatch(session));
            Assert.IsTrue(new SessionTreeview.SearchFilter(mode, "Profile").IsMatch(session));
            Assert.IsTrue(new SessionTreeview.SearchFilter(mode, "").IsMatch(session));
            Assert.IsFalse(new SessionTreeview.SearchFilter(mode, "").IsMatch(null));
            Assert.DoesNotThrow(() => new SessionTreeview.SearchFilter(mode, "test").IsMatch(new SessionData()));
        }

        [Test]
        public void RegexTimeoutDisablesExpressionForRemainingSessions()
        {
            var filter = new SessionTreeview.SearchFilter("Regex", "^(a+)+$");
            Assert.AreEqual(TimeSpan.FromMilliseconds(100), filter.Regex.MatchTimeout);
            Assert.DoesNotThrow(() => filter.IsMatch(new SessionData { SessionName = new string('a', 10000) + "!" }));
            Assert.IsNull(filter.Regex, "Timed-out expressions must not consume another timeout for every session.");
            Assert.IsTrue(filter.IsMatch(new SessionData { SessionName = "AnotherSession" }));
        }

        [TestCase("CaseSensitive", "host", false)]
        [TestCase("CaseInSensitive", "host", true)]
        public void PlainSearchStillMatchesHostWithExpectedCase(string mode, string query, bool expected)
        {
            Assert.AreEqual(expected, new SessionTreeview.SearchFilter(mode, query)
                .IsMatch(new SessionData { SessionName = "Other", Host = "HOST" }));
        }

        [TestCase("server.example:3390", "server.example", 3390)]
        [TestCase("server.example", "server.example", 3389)]
        [TestCase("192.0.2.1:3391", "192.0.2.1", 3391)]
        [TestCase("[2001:db8::1]:3392", "2001:db8::1", 3392)]
        [TestCase("2001:db8::1", "2001:db8::1", 3389)]
        public void RdpHistorySeparatesHostnameAndPort(string endpoint, string host, int port)
        {
            var session = RDPDataHelper.CreateSession(endpoint);
            Assert.AreEqual(host, session.Host);
            Assert.AreEqual(port, session.Port);
            Assert.AreEqual(endpoint, session.SessionName);
            Assert.AreEqual(ConnectionProtocol.RDP, session.Proto);
            string expectedHost = host.Contains(":") ? "[" + host + "]" : host;
            StringAssert.Contains("/v:" + expectedHost + ":" + port, new RDPStartInfo(session, "wfreerdp.exe").Args);
        }

        [TestCase("")]
        [TestCase("server:0")]
        [TestCase("server:65536")]
        [TestCase("server:invalid")]
        [TestCase("user@server")]
        public void InvalidRdpHistoryIsRejected(string endpoint)
        {
            Assert.Throws<ArgumentException>(() => RDPDataHelper.CreateSession(endpoint));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InvalidHostDoesNotStartExternalClient(bool winScp)
        {
            string error;
            Assert.IsFalse(ExternalApplications.TryOpenClient(new SessionData { SessionName = "ProfileOnly" },
                winScp, "client.exe", out error, (file, args) => Assert.Fail("Invalid host was launched.")));
            Assert.IsNotEmpty(error);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedClientLaunchReturnsActionableError(bool winScp)
        {
            string error;
            Assert.IsFalse(ExternalApplications.TryOpenClient(new SessionData { Host = "server.example", Port = 22 },
                winScp, "missing.exe", out error, (file, args) => { throw new Win32Exception(2); }));
            StringAssert.Contains("Tools > Options", error);
        }

        [Test]
        public void ValidExternalClientRequestKeepsArguments()
        {
            string error;
            string launchedArgs = null;
            Assert.IsTrue(ExternalApplications.TryOpenClient(new SessionData { Host = "server.example", Port = 22 },
                true, "client.exe", out error, (file, args) => launchedArgs = args));
            StringAssert.Contains("sftp://server.example:22", launchedArgs);
            Assert.IsNull(error);
        }
    }
}
