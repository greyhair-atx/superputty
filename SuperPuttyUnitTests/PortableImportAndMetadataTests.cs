using System;
using System.Globalization;
using System.IO;
using System.Threading;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Scp;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class PortableImportAndMetadataTests
    {
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-PortableImport-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown() { Directory.Delete(directory, true); }

        private string WriteSession(string name, string protocol)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllLines(path, new[] { @"HostName\server.example\", @"PortNumber\22\", "Protocol\\" + protocol + "\\" });
            return path;
        }

        [TestCase("telnet", ConnectionProtocol.Telnet)]
        [TestCase("rlogin", ConnectionProtocol.Rlogin)]
        [TestCase("raw", ConnectionProtocol.Raw)]
        [TestCase("serial", ConnectionProtocol.Serial)]
        [TestCase("ssh", ConnectionProtocol.SSH)]
        [TestCase("TeLnEt", ConnectionProtocol.Telnet)]
        public void PortableProtocolsImportWithoutCaseSensitivity(string protocol, ConnectionProtocol expected)
        {
            WriteSession("Example", protocol);
            var sessions = SessionData.LoadSessionsFromFolder(directory);
            Assert.AreEqual(1, sessions.Count);
            Assert.AreEqual(expected, sessions[0].Proto);
        }

        [Test]
        public void LockedFileDoesNotPreventOtherPortableSessionsImporting()
        {
            string lockedPath = WriteSession("Locked", "ssh");
            WriteSession("Valid", "telnet");
            using (File.Open(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var sessions = SessionData.LoadSessionsFromFolder(directory);
                Assert.AreEqual(1, sessions.Count);
                Assert.AreEqual("Valid", sessions[0].SessionName);
            }
        }

        [Test]
        public void UnsupportedProtocolDoesNotPreventValidSessionsImporting()
        {
            WriteSession("Unsupported", "unknown");
            WriteSession("Valid", "ssh");
            var sessions = SessionData.LoadSessionsFromFolder(directory);
            Assert.AreEqual(1, sessions.Count);
            Assert.AreEqual("Valid", sessions[0].SessionName);
        }

        [TestCase(0L)]
        [TestCase(2147483648L)]
        [TestCase(4294967296L)]
        public void RemoteListingPreserves64BitByteSize(long bytes)
        {
            var parser = new PscpClient.ScpLineParser();
            BrowserFileInfo file;
            Assert.IsTrue(parser.TryParseFileLine("-rw-r--r-- 1 user group " + bytes.ToString(CultureInfo.InvariantCulture) +
                " Jan  1  2026 large.bin", out file));
            Assert.AreEqual(bytes, file.Size);
        }

        [TestCase("fr-FR", "Jan  1 2026", 2026, 1, 1)]
        [TestCase("de-DE", "May 27 2012", 2012, 5, 27)]
        [TestCase("tr-TR", "Nov 18 18:19", 0, 11, 18)]
        public void EnglishListingTimestampsAreIndependentOfWindowsCulture(string culture, string text, int year, int month, int day)
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                DateTime parsed;
                Assert.IsTrue(new PscpClient.ScpLineParser().TryParseTimestamp(text, out parsed));
                Assert.AreEqual(year == 0 ? DateTime.Now.Year : year, parsed.Year);
                Assert.AreEqual(month, parsed.Month);
                Assert.AreEqual(day, parsed.Day);
                if (year == 0) Assert.AreEqual(new TimeSpan(18, 19, 0), parsed.TimeOfDay);
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }
    }
}
