using NUnit.Framework;
using SuperPutty;

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
    }
}
