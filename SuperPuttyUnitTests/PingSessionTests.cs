using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class PingSessionTests
    {
        [TestCase("192.0.2.1", false)]
        [TestCase("server.example.com", true)]
        [TestCase("fe80::1%12", true)]
        public void PingUsesTemporaryWinCmdSession(string host, bool continuous)
        {
            var source = new SessionData { SessionName = "Original", Host = host, Proto = ConnectionProtocol.SSH };
            var ping = WCMDStartInfo.CreatePingSession(source, continuous);
            var start = new PuttyStartInfo(ping);

            Assert.AreEqual(ConnectionProtocol.WINCMD, ping.Proto);
            StringAssert.EndsWith(" /v:off /k ping.exe " + (continuous ? "-t " : "") + host, start.Args);
            StringAssert.EndsWith("conhost.exe", start.Executable);
            StringAssert.Contains("cmd.exe", start.Args);
            Assert.AreEqual(ConnectionProtocol.SSH, source.Proto);
            Assert.AreEqual("Original", source.SessionName);
            Assert.IsNull(source.ConsoleCommand);
        }

        [TestCase("")]
        [TestCase("host & calc")]
        [TestCase("host\r\ncalc")]
        [TestCase("%COMSPEC%")]
        [TestCase("-t")]
        [TestCase("host\"name")]
        public void UnsafeOrMissingHostCannotReachCommandShell(string host)
        {
            var source = new SessionData { Host = host };
            Assert.IsFalse(WCMDStartInfo.CanPing(source));
            Assert.Throws<System.ArgumentException>(() => WCMDStartInfo.CreatePingSession(source, false));
        }
    }
}
