using System;
using System.Collections.Generic;
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

        [Test]
        public void LoadReportsPhysicalLinesAfterCommentsBlankLinesAndMultilineFields()
        {
            string csv =
                "# Comment before the header\r\n" +
                "\r\n" +
                "SessionName,Host,Protocol,Port,Note\r\n" +
                "First,first.example.com,SSH,22,\"line one\r\n" +
                "line two\"\r\n" +
                "# Comment before the invalid row\r\n" +
                "Bad Port,bad.example.com,SSH,not-a-port,invalid\r\n" +
                "Last,last.example.com,SSH,22,valid\r\n";

            WithCsvFile(csv, delegate(string fileName)
            {
                SessionCsvImportResult result = SessionCsvImporter.Load(fileName);
                string errors = String.Join(Environment.NewLine, result.Errors.ToArray());

                Assert.IsFalse(result.IsValid);
                StringAssert.Contains("Row 7: Port must be a number", errors);
            });
        }

        [Test]
        public void LoadRejectsMalformedRowsAndIncorrectFieldCounts()
        {
            string csv =
                "SessionName,Host,Note\r\n" +
                "Too,Few\r\n" +
                "Malformed,host.example.com,\"unterminated\r\n";

            WithCsvFile(csv, delegate(string fileName)
            {
                SessionCsvImportResult result = SessionCsvImporter.Load(fileName);
                string errors = String.Join(Environment.NewLine, result.Errors.ToArray());

                Assert.IsFalse(result.IsValid);
                Assert.AreEqual(0, result.Sessions.Count);
                StringAssert.Contains("Row 2: expected 3 fields but found 2", errors);
                StringAssert.Contains("Row 3: malformed CSV data", errors);
            });
        }

        [Test]
        public void LoadRejectsEmptyCommentOnlyAndDuplicateSessionPaths()
        {
            WithCsvFile("# comment only\r\n\r\n", delegate(string fileName)
            {
                SessionCsvImportResult result = SessionCsvImporter.Load(fileName);
                StringAssert.Contains("empty or contains only comments", result.Errors[0]);
            });

            string csv =
                "SessionName,Host,Folder\r\n" +
                "Server,one.example.com,Production\\Linux\r\n" +
                "server,two.example.com,/production/linux/\r\n";

            WithCsvFile(csv, delegate(string fileName)
            {
                SessionCsvImportResult result = SessionCsvImporter.Load(fileName);
                string errors = String.Join(Environment.NewLine, result.Errors.ToArray());

                Assert.IsFalse(result.IsValid);
                StringAssert.Contains("Row 3: duplicate session path", errors);
            });
        }

        [Test]
        public void LoadParsesProtocolAliasesUtf8AndOptionalProperties()
        {
            string csv =
                "SessionName,Host,Protocol,Username,ExtraArgs,Note,ImageKey,SPSLFileName,RemotePath,LocalPath\r\n" +
                "PowerShell Local,localhost,PowerShell,user,-NoLogo,Grüße,windows,test.spsl,/remote,C:\\local\r\n" +
                "Command Local,localhost,Win Command Prompt,,,,,,,\r\n";

            WithCsvFile(csv, delegate(string fileName)
            {
                SessionCsvImportResult result = SessionCsvImporter.Load(fileName);

                Assert.IsTrue(result.IsValid, String.Join(Environment.NewLine, result.Errors.ToArray()));
                Assert.AreEqual(2, result.Sessions.Count);
                Assert.AreEqual(ConnectionProtocol.PS, result.Sessions[0].Proto);
                Assert.AreEqual(0, result.Sessions[0].Port);
                Assert.AreEqual("Grüße", result.Sessions[0].Note);
                Assert.AreEqual("windows", result.Sessions[0].ImageKey);
                Assert.AreEqual("test.spsl", result.Sessions[0].SPSLFileName);
                Assert.AreEqual("/remote", result.Sessions[0].RemotePath);
                Assert.AreEqual("C:\\local", result.Sessions[0].LocalPath);
                Assert.AreEqual(ConnectionProtocol.WINCMD, result.Sessions[1].Proto);
            });
        }

        [Test]
        public void ImportSessionsRollsBackMemoryAndInputNamesWhenPersistenceFails()
        {
            string originalId = "Rollback-" + Guid.NewGuid().ToString("N");
            SessionData session = new SessionData
            {
                SessionId = originalId,
                SessionName = originalId,
                Host = "rollback.example.com"
            };

            bool threw = false;
            try
            {
                global::SuperPutty.SuperPuTTY.ImportSessions(
                    new List<SessionData> { session },
                    "Imported",
                    delegate { throw new IOException("Simulated persistence failure"); });
            }
            catch (IOException)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "The persistence exception should be propagated.");
            Assert.IsNull(global::SuperPutty.SuperPuTTY.GetSessionById("Imported/" + originalId));
            Assert.AreEqual(originalId, session.SessionId);
            Assert.AreEqual(originalId, session.SessionName);
        }

        [Test]
        public void SaveSessionsToFileReplacesExistingFileAndLeavesNoTemporaryFile()
        {
            WithTemporaryDirectory(delegate(string directory)
            {
                string fileName = Path.Combine(directory, "Sessions.XML");
                File.WriteAllText(fileName, "old contents");
                List<SessionData> sessions = new List<SessionData>
                {
                    new SessionData
                    {
                        SessionId = "Saved",
                        SessionName = "Saved",
                        Host = "saved.example.com"
                    }
                };

                SessionData.SaveSessionsToFile(sessions, fileName);

                List<SessionData> loaded = SessionData.LoadSessionsFromFile(fileName);
                Assert.AreEqual(1, loaded.Count);
                Assert.AreEqual("Saved", loaded[0].SessionId);
                Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
            });
        }

        [Test]
        public void SaveSessionsToFilePreservesExistingFileWhenReplacementFails()
        {
            WithTemporaryDirectory(delegate(string directory)
            {
                string fileName = Path.Combine(directory, "Sessions.XML");
                const string originalContents = "original contents";
                File.WriteAllText(fileName, originalContents);
                File.SetAttributes(fileName, FileAttributes.ReadOnly);

                bool threw = false;
                try
                {
                    SessionData.SaveSessionsToFile(new List<SessionData>(), fileName);
                }
                catch (UnauthorizedAccessException)
                {
                    threw = true;
                }
                finally
                {
                    File.SetAttributes(fileName, FileAttributes.Normal);
                }

                Assert.IsTrue(threw, "Replacing a read-only destination should fail.");
                Assert.AreEqual(originalContents, File.ReadAllText(fileName));
                Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
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

        private static void WithTemporaryDirectory(Action<string> test)
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPuttyTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                test(directory);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    foreach (string fileName in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(fileName, FileAttributes.Normal);
                    }
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
