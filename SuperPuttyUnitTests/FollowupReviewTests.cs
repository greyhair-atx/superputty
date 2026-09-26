using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Utils;
using SuperPuTTY.Scripting;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class FollowupReviewTests
    {
        [TestCase("SLEEP invalid")]
        [TestCase("CLOSESESSION test")]
        [TestCase("UNSUPPORTED test")]
        public void ScriptFailureStopsWorkerAndReportsTheFailingLine(string command)
        {
            int reported = 0;
            using (var window = new Form())
            {
            var worker = SPSL.CreateExecutionThread(new ExecuteScriptEventArgs { Handle = window.Handle },
                new[] { "# comment", command, "SLEEP 60000" }, line => reported = line);
            worker.Start();
            Assert.IsTrue(worker.Join(2000), "An invalid command must stop before executing later lines.");
            Assert.AreEqual(2, reported);
            }
        }

        [Test]
        public void RdpAuthenticationCanOutliveWindowDiscoveryTimeout()
        {
            Assert.IsFalse(ApplicationPanel.CaptureTimedOut(ConnectionProtocol.RDP, true, 120000, 30000));
            Assert.IsTrue(ApplicationPanel.CaptureTimedOut(ConnectionProtocol.RDP, false, 30001, 30000));
            Assert.IsTrue(ApplicationPanel.CaptureTimedOut(ConnectionProtocol.SSH, true, 30001, 30000));
            Assert.IsFalse(ApplicationPanel.CaptureTimedOut(ConnectionProtocol.RDP, false, 1000, 30000));
        }

        [Test, Apartment(ApartmentState.STA)]
        public void ReparentingVerifiesNativeParentAndRejectsInvalidWindows()
        {
            using (var host = new Form())
            using (var child = new Form())
            {
                ApplicationPanel.ReparentWindow(child.Handle, host.Handle);
                Assert.AreEqual(host.Handle, NativeMethods.GetParent(child.Handle));
                Assert.Throws<InvalidOperationException>(() => ApplicationPanel.ReparentWindow(IntPtr.Zero, host.Handle));
                Assert.Throws<InvalidOperationException>(() => ApplicationPanel.ReparentWindow(child.Handle, IntPtr.Zero));
            }
        }

        [Test]
        public void RemoteScriptReaderEnforcesSizeLimitAndPreservesUtf8()
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("SENDLINE café")))
                Assert.AreEqual("SENDLINE café", RemoteSpslLoader.ReadScriptAsync(stream, CancellationToken.None).GetAwaiter().GetResult());
            using (var oversized = new MemoryStream(new byte[RemoteSpslLoader.MaximumScriptBytes + 1]))
                Assert.ThrowsAsync<InvalidOperationException>(() => RemoteSpslLoader.ReadScriptAsync(oversized, CancellationToken.None));
        }

        private sealed class SlowStream : MemoryStream
        {
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellation)
            {
                await Task.Delay(20, cancellation);
                buffer[offset] = 65;
                return 1;
            }
        }

        [Test]
        public void SlowScriptBodyHonorsCancellationDespiteContinuedProgress()
        {
            using (var stream = new SlowStream())
            using (var cancellation = new CancellationTokenSource(150))
            {
                Task<string> read = RemoteSpslLoader.ReadScriptAsync(stream, cancellation.Token);
                Assert.IsFalse(read.IsCompleted, "Reading must yield to the caller.");
                Assert.CatchAsync<OperationCanceledException>(async () => await read);
            }
        }

        [Test]
        public void CanceledDownloadDoesNotStartANetworkRequest()
        {
            Assert.CatchAsync<OperationCanceledException>(() => RemoteSpslLoader.DownloadAsync(
                new Uri("https://invalid.invalid/script"), new CancellationToken(true)));
        }

        [Test]
        public void BackupRetentionUsesRequestedCount()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPuttyBackups-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string file = Path.Combine(directory, "Sessions.xml");
                File.WriteAllText(file, "<ArrayOfSessionData />");
                foreach (string stamp in new[] { "20000101_010000", "20000101_130000", "20000101_140000" })
                    File.WriteAllText(Path.Combine(directory, "Sessions." + stamp + ".XML"), "<ArrayOfSessionData />");
                typeof(SessionData).GetMethod("BackUpFiles", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { file, 2 });
                string[] backups = Directory.GetFiles(directory, "Sessions.*.XML");
                Assert.AreEqual(2, backups.Length);
                Assert.IsTrue(backups.Any(path => path.EndsWith("20000101_140000.XML")));
                Assert.IsFalse(backups.Any(path => path.EndsWith("20000101_130000.XML")));
            }
            finally
            {
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }
}
