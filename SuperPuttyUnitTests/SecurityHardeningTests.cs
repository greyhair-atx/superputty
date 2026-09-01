using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Scp;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class SecurityHardeningTests
    {
        [Test]
        public void VncPasswordArgumentsRespectPlaintextCredentialPolicy()
        {
            bool original = SuperPutty.SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg;
            SessionData session = new SessionData
            {
                Host = "vnc.example.com",
                Port = 5900,
                Password = "secret value",
                ExtraArgs = "-password=extra-secret"
            };

            try
            {
                SuperPutty.SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg = false;
                VNCStartInfo disabled = new VNCStartInfo(session);
                StringAssert.DoesNotContain("secret value", disabled.Args);
                StringAssert.DoesNotContain("extra-secret", disabled.Args);
                StringAssert.DoesNotContain("-password", disabled.Args);

                SuperPutty.SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg = true;
                VNCStartInfo enabled = new VNCStartInfo(session);
                StringAssert.Contains("-password=\"secret value\"", enabled.Args);
                StringAssert.DoesNotContain("secret value", enabled.ArgsForLog);
                StringAssert.DoesNotContain("extra-secret", enabled.Args);
            }
            finally
            {
                SuperPutty.SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg = original;
            }
        }

        [Test]
        public void PscpCopyPasswordArgumentsRespectPlaintextCredentialPolicy()
        {
            bool original = SuperPutty.SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg;
            SessionData session = new SessionData
            {
                Host = "ssh.example.com",
                Port = 22,
                Username = "user",
                Password = "secret value",
                ExtraArgs = "-batch -pw bypass-secret"
            };
            List<BrowserFileInfo> source = new List<BrowserFileInfo>
            {
                new BrowserFileInfo { Path = @"C:\source file.txt", Source = SourceType.Local }
            };
            BrowserFileInfo target = new BrowserFileInfo { Path = "/tmp", Source = SourceType.Remote };

            try
            {
                SuperPutty.SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg = false;
                string disabled = PscpClient.ToArgs(session, session.Password, source, target);
                StringAssert.DoesNotContain("secret value", disabled);
                StringAssert.DoesNotContain("bypass-secret", disabled);
                StringAssert.DoesNotContain("-pw", disabled);

                SuperPutty.SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg = true;
                string enabled = PscpClient.ToArgs(session, session.Password, source, target);
                StringAssert.Contains("-pw \"secret value\"", enabled);
            }
            finally
            {
                SuperPutty.SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg = original;
            }
        }

        [Test]
        public void CancelingATransferDoesNotWaitForTheWorkerThread()
        {
            FileTransfer transfer = new FileTransfer(new PscpOptions(), new FileTransferRequest());
            using (ManualResetEvent releaseWorker = new ManualResetEvent(false))
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Thread worker = new Thread(() => releaseWorker.WaitOne()) { IsBackground = true };
                worker.Start();

                SetPrivateField(transfer, "thread", worker);
                SetPrivateField(transfer, "cancellation", cancellation);
                SetPrivateField(transfer, "status", FileTransfer.Status.Running);

                Stopwatch stopwatch = Stopwatch.StartNew();
                transfer.Cancel();
                stopwatch.Stop();

                releaseWorker.Set();
                worker.Join(2000);

                Assert.Less(stopwatch.ElapsedMilliseconds, 500, "Cancellation must not join the worker on the UI thread.");
                Assert.AreEqual(FileTransfer.Status.Canceling, transfer.TransferStatus);
                Assert.IsFalse(FileTransfer.CanRestart(transfer.TransferStatus));
            }
        }

        [TestCase(ConnectionProtocol.SSH2)]
        [TestCase(ConnectionProtocol.SSHNet)]
        public void LegacySshSessionsUseTheSupportedSshProtocol(ConnectionProtocol protocol)
        {
            SessionData session = new SessionData
            {
                Proto = protocol,
                Host = "ssh.example.com",
                Port = 22
            };

            PuttyStartInfo startInfo = new PuttyStartInfo(session);

            StringAssert.StartsWith("-ssh ", startInfo.Args);
            StringAssert.DoesNotContain("-sshnet", startInfo.Args);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Expected private field " + fieldName);
            field.SetValue(target, value);
        }
    }
}
