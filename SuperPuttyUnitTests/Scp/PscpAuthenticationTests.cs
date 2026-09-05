using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using SuperPutty.Scp;

namespace SuperPuttyUnitTests.Scp
{
    [TestFixture]
    public class PscpAuthenticationTests
    {
        [TestCase("user@example.com's password:")]
        [TestCase("Password:")]
        [TestCase("PASSWORD:")]
        public void RecognizesPasswordPromptsRegardlessOfCase(string prompt)
        {
            Assert.IsTrue(PscpClient.IsPasswordPrompt(prompt));
        }

        [Test]
        public void SuppliesPasswordThroughStandardInputWhenCommandLinePasswordsAreDisabled()
        {
            StringWriter input = new StringWriter();

            PscpClient.AuthenticationPromptAction action = PscpClient.HandleAuthenticationPrompt(
                "user@example.com's password:",
                "secret value",
                false,
                false,
                input);

            Assert.AreEqual(PscpClient.AuthenticationPromptAction.PasswordSubmitted, action);
            Assert.AreEqual("secret value" + input.NewLine, input.ToString());
        }

        [Test]
        public void RequestsCredentialsWhenNoPasswordIsAvailableOrPreviousPasswordWasRejected()
        {
            Assert.AreEqual(
                PscpClient.AuthenticationPromptAction.RetryAuthentication,
                PscpClient.HandleAuthenticationPrompt("Password:", null, false, false, TextWriter.Null));
            Assert.AreEqual(
                PscpClient.AuthenticationPromptAction.RetryAuthentication,
                PscpClient.HandleAuthenticationPrompt("Password:", "bad password", false, true, TextWriter.Null));
        }

        [Test]
        public void ReaderReportsPasswordPromptWithoutWaitingForNewline()
        {
            byte[] data = Encoding.UTF8.GetBytes("user@example.com's password:");
            string received = null;
            using (ManualResetEventSlim signaled = new ManualResetEventSlim())
            using (MemoryStream stream = new MemoryStream(data))
            using (StreamReader streamReader = new StreamReader(stream))
            using (PscpClient.AsyncStreamReader reader = new PscpClient.AsyncStreamReader(
                "ERR",
                streamReader,
                line =>
                {
                    received = line;
                    signaled.Set();
                    return false;
                }))
            {
                Assert.IsTrue(signaled.Wait(1000), "Password prompt was not reported promptly.");
                Assert.AreEqual("user@example.com's password:", received);
            }
        }
    }
}
