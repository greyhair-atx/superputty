using System;
using System.IO;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class RDPStartInfoTests
    {
        [Test]
        public void MstscUsesSmartSizingConnectionFileInsteadOfSpan()
        {
            SessionData session = CreateSession();
            session.ExtraArgs = "/admin";

            RDPStartInfo startInfo = new RDPStartInfo(session, @"C:\Windows\System32\mstsc.exe");

            StringAssert.Contains(" /v:rdp.example.com:3390", startInfo.Args);
            StringAssert.EndsWith(" /admin", startInfo.Args);
            StringAssert.DoesNotContain("/span", startInfo.Args);

            int closingQuote = startInfo.Args.IndexOf('"', 1);
            Assert.Greater(closingQuote, 1);
            string connectionFile = startInfo.Args.Substring(1, closingQuote - 1);
            Assert.IsTrue(File.Exists(connectionFile));
            StringAssert.Contains("smart sizing:i:1", File.ReadAllText(connectionFile));
            StringAssert.Contains("use multimon:i:0", File.ReadAllText(connectionFile));
        }

        [Test]
        public void FreeRdpUsesNativeSmartSizingAndSeparatesArguments()
        {
            SessionData session = CreateSession();
            session.Port = 0;
            session.ExtraArgs = "  +clipboard  ";

            RDPStartInfo startInfo = new RDPStartInfo(session, "WFREERDP.EXE");

            StringAssert.Contains("/smart-sizing", startInfo.Args);
            StringAssert.Contains(" /v:rdp.example.com /u:test-user +clipboard", startInfo.Args);
        }

        private static SessionData CreateSession()
        {
            return new SessionData
            {
                Host = "rdp.example.com",
                Port = 3390,
                Username = "test-user"
            };
        }
    }
}
