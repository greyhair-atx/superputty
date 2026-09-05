using System.IO;
using System.IO.Pipes;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using NUnit.Framework;
using SuperPutty.Data;
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

        [Test]
        public void BlankPasswordUsesBatchModeForInitialAuthenticationAttempt()
        {
            SessionData session = new SessionData
            {
                Username = "sysadmin",
                Host = "example.invalid",
                Port = 22
            };

            string initialArgs = PscpClient.ToArgs(session, "", "/home/sysadmin");
            string authenticatedArgs = PscpClient.ToArgs(session, "secret", "/home/sysadmin");

            StringAssert.Contains("-batch", initialArgs);
            StringAssert.Contains("-batch", authenticatedArgs);
        }

        [Test]
        public void PrivateKeyFileIsQuotedForListingsAndTransfers()
        {
            SessionData session = new SessionData
            {
                Username = "sysadmin",
                Host = "example.invalid",
                Port = 22,
                PrivateKeyFile = @"C:\Keys\Gitea Login.ppk"
            };

            string listingArgs = PscpClient.ToArgs(session, "", "/home/sysadmin");
            string transferArgs = PscpClient.ToArgs(
                session,
                "",
                new List<BrowserFileInfo> { new BrowserFileInfo { Path = @"C:\Temp\file.txt", Source = SourceType.Local } },
                new BrowserFileInfo { Path = "/home/sysadmin/", Source = SourceType.Remote });

            StringAssert.Contains("-i \"C:\\Keys\\Gitea Login.ppk\"", listingArgs);
            StringAssert.Contains("-i \"C:\\Keys\\Gitea Login.ppk\"", transferArgs);
        }

        [TestCase("-v", true)]
        [TestCase("-batch -v -P 22", true)]
        [TestCase("-V", true)]
        [TestCase("-verbose", false)]
        [TestCase("-batch", false)]
        public void DetectsOnlyPscpVerboseSwitch(string arguments, bool expected)
        {
            Assert.AreEqual(expected, PscpClient.HasVerboseArgument(arguments));
        }

        [Test]
        public void PasswordPipeProvidesPasswordWithoutCreatingAFile()
        {
            string passwordPipePath;
            using (PscpClient.PscpPasswordPipe passwordPipe =
                PscpClient.PscpPasswordPipe.Create("secret value", false))
            {
                Assert.IsNotNull(passwordPipe);
                passwordPipePath = passwordPipe.PipePath;
                const string pipePrefix = @"\\.\pipe\";
                StringAssert.StartsWith(pipePrefix + "SuperPuTTY-pscp-", passwordPipePath);
                using (NamedPipeClientStream client = new NamedPipeClientStream(
                    ".",
                    passwordPipePath.Substring(pipePrefix.Length),
                    PipeDirection.In))
                {
                    client.Connect(1000);
                    using (StreamReader reader = new StreamReader(client))
                    {
                        Assert.AreEqual(
                            "secret value" + System.Environment.NewLine,
                            reader.ReadToEnd());
                    }
                }

                SessionData session = new SessionData
                {
                    Username = "sysadmin",
                    Host = "example.invalid",
                    Port = 22
                };
                string arguments = PscpClient.ToArgs(
                    session,
                    "secret value",
                    "/home/sysadmin",
                    passwordPipePath);

                StringAssert.Contains("-pwfile", arguments);
                StringAssert.Contains(passwordPipePath, arguments);
                StringAssert.DoesNotContain("secret value", arguments);
            }
        }

        [Test]
        public void PlainTextPasswordModeDoesNotCreatePasswordPipe()
        {
            Assert.IsNull(PscpClient.PscpPasswordPipe.Create("secret value", true));
            Assert.IsNull(PscpClient.PscpPasswordPipe.Create("", false));
        }
    }
}
