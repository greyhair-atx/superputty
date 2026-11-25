using System.Collections.Generic;
using NUnit.Framework;
using System.Diagnostics;
using log4net;
using SuperPutty.Data;
using SuperPutty.Scp;
using SuperPutty.Utils;
using System;
using System.IO;

namespace SuperPuttyUnitTests.Scp
{
    /// <summary>
    /// Basic SCP tests to test underlying process invocation limitations/quirks
    /// </summary>
    [TestFixture, Category("NetworkTest")]
    class ScpTests
    {
        [SetUp]
        public void RequireTestServer()
        {
            ScpConfig.RequireTestServer();
        }

        [Test]
        public void ListDirSuccess()
        {
            var result = ListDirectory(ScpConfig.Password, ScpConfig.KnownHost);
            Assert.AreEqual(0, result.ExitCode, result.Error);
            Assert.IsNotEmpty(result.Output);
        }

        [Test]
        public void ListDirBadPassword()
        {
            var result = ListDirectory(ScpConfig.Password + "zzz", ScpConfig.KnownHost);
            Assert.AreNotEqual(0, result.ExitCode, "PSCP accepted the incorrect password.");
        }

        [Test]
        public void ListDirHostNoKey()
        {
            var result = ListDirectory(ScpConfig.Password, ScpConfig.UnKnownHost);
            Assert.AreNotEqual(0, result.ExitCode);
            Assert.That(result.Error, Does.Contain("host key is not cached").IgnoreCase);
        }

        private static (int ExitCode, string Output, string Error) ListDirectory(string password, string host)
        {
            using (var credentials = PscpClient.PscpPasswordPipe.Create(password, false))
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ScpConfig.PscpLocation,
                    Arguments = "-ls -batch -P " + ScpConfig.Port + " -pwfile " +
                        CommandLineOptions.QuoteArgument(credentials.PipePath) + " " +
                        CommandLineOptions.QuoteArgument(ScpConfig.UserName + "@" + host + ":."),
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                }
            })
            {
                Assert.IsTrue(process.Start());
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                    Assert.Fail("PSCP did not finish within 15 seconds.");
                }
                return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
            }
        }
    }

    [TestFixture, Category("NetworkTest")]
    public class PscpClientListDirTests
    {
        [SetUp]
        public void RequireTestServer()
        {
            ScpConfig.RequireTestServer();
        }

        private static readonly ILog Log = LogManager.GetLogger(typeof(PscpClientListDirTests));

        [Test]
        public void ListDirSuccess()
        {
            SessionData session = new SessionData
            {
                Username = ScpConfig.UserName, 
                Password = ScpConfig.Password, 
                Host = ScpConfig.KnownHost, 
                Port = ScpConfig.Port
            };

            PscpClient client = new PscpClient(ScpConfig.DefaultOptions, session);

            ListDirectoryResult res = client.ListDirectory(new BrowserFileInfo { Path = "." });

            Assert.AreEqual(ResultStatusCode.Success, res.StatusCode);
            Assert.Greater(res.Files.Count, 0);
            foreach (BrowserFileInfo file in res.Files)
            {
                Log.Info(file);
            }

            Log.InfoFormat("Result: {0}", res);
        }

        [Test]
        public void ListDirBadPassword()
        {
            SessionData session = new SessionData
            {
                Username = ScpConfig.UserName,
                Password = ScpConfig.Password + "xxx",
                Host = ScpConfig.KnownHost,
                Port = ScpConfig.Port
            };

            PscpClient client = new PscpClient(ScpConfig.DefaultOptions, session);

            ListDirectoryResult res = client.ListDirectory(new BrowserFileInfo { Path = "." });

            Assert.AreEqual(ResultStatusCode.RetryAuthentication, res.StatusCode);
            Assert.AreEqual(res.Files.Count, 0);

            Log.InfoFormat("Result: {0}", res);
        }

        [Test]
        public void ListDirHostNoKey()
        {
            SessionData session = new SessionData
            {
                Username = ScpConfig.UserName,
                Password = ScpConfig.Password,
                Host = ScpConfig.UnKnownHost,
                Port = ScpConfig.Port
            };

            PscpClient client = new PscpClient(ScpConfig.DefaultOptions, session);

            ListDirectoryResult res = client.ListDirectory(new BrowserFileInfo { Path = "." });

            Assert.AreEqual(ResultStatusCode.Error, res.StatusCode);
            Assert.AreEqual(res.Files.Count, 0);

            Log.InfoFormat("Result: {0}", res);
        }

        [Test]
        public void ListDirBadPath()
        {
            SessionData session = new SessionData
            {
                Username = ScpConfig.UserName,
                Password = ScpConfig.Password,
                Host = ScpConfig.KnownHost,
                Port = ScpConfig.Port
            };

            PscpClient client = new PscpClient(ScpConfig.DefaultOptions, session);

            ListDirectoryResult res = client.ListDirectory(new BrowserFileInfo { Path = "some_non_existant_dir" });

            Assert.AreEqual(ResultStatusCode.Error, res.StatusCode);
            Assert.AreEqual(0, res.Files.Count);
            Assert.IsTrue(res.ErrorMsg != null && res.ErrorMsg.StartsWith("Unable to open"));
            Log.InfoFormat("Result: {0}", res);
        }
    }

    [TestFixture, Category("NetworkTest")]
    public class PscpClientTransferTests
    {
        [SetUp]
        public void RequireTestServer()
        {
            ScpConfig.RequireTestServer();
        }

        [Test]
        public void LocalToRemote()
        {
            SessionData session = new SessionData
            {
                Username = ScpConfig.UserName,
                Password = ScpConfig.Password,
                Host = ScpConfig.KnownHost,
                Port = ScpConfig.Port
            };

            string directory = Path.Combine(Path.GetTempPath(), "SuperPuttySshProbe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "superputty-network-test.txt");
            try
            {
                File.WriteAllText(file, "SuperPuTTY SCP test");
                var sourceFiles = new List<BrowserFileInfo> { new BrowserFileInfo(new FileInfo(file)) };
                var target = new BrowserFileInfo { Path = ".", Source = SourceType.Remote, Type = FileType.Directory };
                var client = new PscpClient(ScpConfig.DefaultOptions, session);
                FileTransferResult result = client.CopyFiles(sourceFiles, target, null);
                Assert.AreEqual(ResultStatusCode.Success, result.StatusCode, result.ErrorMsg);
            }
            finally
            {
                File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }

    public class ScpConfig
    {
        public static readonly string PscpLocation = Environment.GetEnvironmentVariable("SUPERPUTTY_TEST_PSCP");
        public static readonly string UserName = Environment.GetEnvironmentVariable("SUPERPUTTY_TEST_SSH_USER");
        public static readonly string Password = Environment.GetEnvironmentVariable("SUPERPUTTY_TEST_SSH_PASSWORD");
        public static readonly string KnownHost = Environment.GetEnvironmentVariable("SUPERPUTTY_TEST_SSH_HOST");
        public static readonly string UnKnownHost = Environment.GetEnvironmentVariable("SUPERPUTTY_TEST_SSH_UNTRUSTED_HOST");
        public static ushort Port { get; private set; } = 22;

        public static PscpOptions DefaultOptions = new PscpOptions { PscpLocation = PscpLocation };

        internal static void RequireTestServer()
        {
            if (String.IsNullOrWhiteSpace(PscpLocation) || String.IsNullOrWhiteSpace(KnownHost) ||
                String.IsNullOrWhiteSpace(UnKnownHost) || String.IsNullOrWhiteSpace(UserName) || String.IsNullOrEmpty(Password))
                Assert.Ignore("Set the PSCP executable and disposable SSH test account environment variables documented in docs/testing.md.");

            Assert.IsTrue(File.Exists(PscpLocation), "The PSCP executable supplied by SUPERPUTTY_TEST_PSCP does not exist.");
            string configuredPort = Environment.GetEnvironmentVariable("SUPERPUTTY_TEST_SSH_PORT");
            if (!String.IsNullOrWhiteSpace(configuredPort))
            {
                ushort port;
                Assert.IsTrue(UInt16.TryParse(configuredPort, out port) && port > 0, "The SSH test port must be between 1 and 65535.");
                Port = port;
            }
        }
    }
}
