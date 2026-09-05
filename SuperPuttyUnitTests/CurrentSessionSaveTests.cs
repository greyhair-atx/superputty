using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using System.IO;
using System.Xml.Serialization;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class CurrentSessionSaveTests
    {
        [Test]
        public void BuildsRenamedSessionIdInExistingFolder()
        {
            string sessionId;
            string error;

            bool valid = frmSuperPutty.TryBuildCurrentSessionId(
                "Production/Old Name",
                "New Name",
                out sessionId,
                out error);

            Assert.IsTrue(valid);
            Assert.AreEqual("Production/New Name", sessionId);
            Assert.AreEqual(string.Empty, error);
        }

        [Test]
        public void BuildsRootIdForNewQuickConnectSession()
        {
            string sessionId;
            string error;

            bool valid = frmSuperPutty.TryBuildCurrentSessionId(
                "Original Host",
                "Saved Host",
                out sessionId,
                out error);

            Assert.IsTrue(valid);
            Assert.AreEqual("Saved Host", sessionId);
            Assert.AreEqual(string.Empty, error);
        }

        [TestCase(null, "Empty name")]
        [TestCase("   ", "Empty name")]
        [TestCase("Folder/Name", "Invalid character ( / ) in name")]
        public void RejectsInvalidSessionNames(string name, string expectedError)
        {
            string sessionId;
            string error;

            bool valid = frmSuperPutty.TryBuildCurrentSessionId(
                "Existing",
                name,
                out sessionId,
                out error);

            Assert.IsFalse(valid);
            Assert.IsNull(sessionId);
            Assert.AreEqual(expectedError, error);
        }

        [Test]
        public void ScpSessionsUseTheSshPortAndRemainDistinctFromSsh()
        {
            Assert.AreNotEqual(ConnectionProtocol.SSH, ConnectionProtocol.SCP);
            Assert.AreEqual(22, dlgEditSession.GetDefaultPort(ConnectionProtocol.SCP));
            Assert.IsTrue(SuperPutty.SuperPuTTY.IsScpSession(new SessionData { Proto = ConnectionProtocol.SCP }));
            Assert.IsFalse(SuperPutty.SuperPuTTY.IsScpSession(new SessionData { Proto = ConnectionProtocol.SSH }));
        }

        [Test]
        public void ScpProtocolSurvivesSessionSerialization()
        {
            XmlSerializer serializer = new XmlSerializer(typeof(SessionData));
            SessionData original = new SessionData
            {
                SessionName = "SCP Test",
                SessionId = "SCP Test",
                Proto = ConnectionProtocol.SCP,
                PrivateKeyFile = @"C:\Keys\gitea.ppk"
            };

            string xml;
            using (StringWriter writer = new StringWriter())
            {
                serializer.Serialize(writer, original);
                xml = writer.ToString();
            }

            using (StringReader reader = new StringReader(xml))
            {
                SessionData restored = (SessionData)serializer.Deserialize(reader);
                Assert.AreEqual(ConnectionProtocol.SCP, restored.Proto);
                Assert.AreEqual(@"C:\Keys\gitea.ppk", restored.PrivateKeyFile);
            }
        }

        [Test]
        public void LogFileLocationUsesTheTemporaryDirectory()
        {
            Assert.AreEqual(Path.GetTempPath(), frmSuperPutty.GetLogFileDirectory());
        }

        [Test]
        public void AutomaticLayoutDoesNotRestorePreviousSessionDocuments()
        {
            LayoutData automaticLayout = new LayoutData("AutoRestoreLayout.XML")
            {
                Name = LayoutData.AutoRestore
            };
            LayoutData manuallySavedLayout = new LayoutData("Work.xml");

            Assert.IsFalse(frmSuperPutty.ShouldRestoreSessionDocuments(automaticLayout));
            Assert.IsTrue(frmSuperPutty.ShouldRestoreSessionDocuments(manuallySavedLayout));
            Assert.IsTrue(frmSuperPutty.ShouldRestoreSessionDocuments(null));
        }
    }
}
