using System;
using System.IO;
using NUnit.Framework;
using SuperPutty.Data;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class SessionCsvImporterTests
    {
        [Test]
        public void LoadParsesCommentsQuotedValuesFoldersAndDefaults()
        {
            string csv =
                "# Example comment\r\n" +
                "SessionName,Host,Protocol,Port,Username,Folder,PuttySession,Note\r\n" +
                "\"Production, Primary\",prod.example.com,SSH,,admin,Production,,\"Primary, monitored server\"\r\n" +
                "Legacy Profile,,Telnet,,,Legacy,Legacy Router,Profile-based connection\r\n";

            WithCsvFile(csv, delegate(string fileName)
            {
                SessionCsvImportResult result = SessionCsvImporter.Load(fileName);

                Assert.IsTrue(result.IsValid, String.Join(Environment.NewLine, result.Errors.ToArray()));
                Assert.AreEqual(2, result.Sessions.Count);

                SessionData production = result.Sessions[0];
                Assert.AreEqual("Production, Primary", production.SessionName);
                Assert.AreEqual("Production/Production, Primary", production.SessionId);
                Assert.AreEqual("prod.example.com", production.Host);
                Assert.AreEqual(ConnectionProtocol.SSH, production.Proto);
                Assert.AreEqual(22, production.Port);
                Assert.AreEqual(PuttyDataHelper.SessionDefaultSettings, production.PuttySession);
                Assert.AreEqual("Primary, monitored server", production.Note);

                SessionData profile = result.Sessions[1];
                Assert.AreEqual("Legacy/Legacy Profile", profile.SessionId);
                Assert.AreEqual(String.Empty, profile.Host);
                Assert.AreEqual(ConnectionProtocol.Telnet, profile.Proto);
                Assert.AreEqual(23, profile.Port);
                Assert.AreEqual("Legacy Router", profile.PuttySession);
            });
        }

        [Test]
        public void LoadCollectsAllRowErrorsBeforeImportCanProceed()
        {
            string csv =
                "SessionName,Host,Protocol,Port,Folder\r\n" +
                ",host.example.com,SSH,22,Servers\r\n" +
                "Missing Target,,SSH,22,Servers\r\n" +
                "Bad Protocol,host.example.com,NotAProtocol,22,Servers\r\n" +
                "Bad Port,host.example.com,SSH,70000,Servers\r\n" +
                "Duplicate,one.example.com,SSH,22,Servers\r\n" +
                "Duplicate,two.example.com,SSH,22,Servers\r\n";

            WithCsvFile(csv, delegate(string fileName)
            {
                SessionCsvImportResult result = SessionCsvImporter.Load(fileName);
                string errors = String.Join(Environment.NewLine, result.Errors.ToArray());

                Assert.IsFalse(result.IsValid);
                Assert.AreEqual(0, result.Sessions.Count, "Invalid CSV files must not expose a partial import list.");
                StringAssert.Contains("Row 2: SessionName is required", errors);
                StringAssert.Contains("Row 3: either Host or PuttySession is required", errors);
                StringAssert.Contains("Row 4: unsupported Protocol", errors);
                StringAssert.Contains("Row 5: Port must be a number", errors);
                StringAssert.Contains("Row 7: duplicate session path", errors);
            });
        }

        [Test]
        public void LoadRejectsInvalidHeadersBeforeParsingSessions()
        {
            string csv = "Name,Host,Unexpected\r\nServer,host.example.com,value\r\n";

            WithCsvFile(csv, delegate(string fileName)
            {
                SessionCsvImportResult result = SessionCsvImporter.Load(fileName);
                string errors = String.Join(Environment.NewLine, result.Errors.ToArray());

                Assert.IsFalse(result.IsValid);
                Assert.AreEqual(0, result.Sessions.Count);
                StringAssert.Contains("unsupported column 'Name'", errors);
                StringAssert.Contains("unsupported column 'Unexpected'", errors);
                StringAssert.Contains("required 'SessionName' column is missing", errors);
            });
        }

        private static void WithCsvFile(string contents, Action<string> test)
        {
            string fileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                File.WriteAllText(fileName, contents);
                test(fileName);
            }
            finally
            {
                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }
            }
        }
    }
}
