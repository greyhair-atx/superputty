using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Scp;

namespace SuperPuttyUnitTests.Scp
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class ScpRecoveryTests
    {
        private sealed class QueueContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<Action> callbacks = new ConcurrentQueue<Action>();
            public override void Post(SendOrPostCallback callback, object state)
            {
                callbacks.Enqueue(() => callback(state));
            }
            internal void Drain()
            {
                Action callback;
                while (callbacks.TryDequeue(out callback)) callback();
            }
        }

        private sealed class FailingFirstListing : IBrowserModel
        {
            internal readonly List<BrowserFileInfo> Requests = new List<BrowserFileInfo>();
            public ListDirectoryResult ListDirectory(SessionData session, BrowserFileInfo path)
            {
                Requests.Add(path);
                var result = new ListDirectoryResult(path)
                {
                    StatusCode = Requests.Count == 1 ? ResultStatusCode.Error : ResultStatusCode.Success,
                    ErrorMsg = Requests.Count == 1 ? "Temporary connection failure" : null
                };
                result.Files.Add(new BrowserFileInfo { Name = "recovered.txt", Path = "/recovered.txt", Type = FileType.File });
                return result;
            }
        }

        private static void WaitForListing(BrowserPresenter presenter, QueueContext context)
        {
            var elapsed = Stopwatch.StartNew();
            while (presenter.ViewModel.BrowserState != BrowserState.Ready && elapsed.ElapsedMilliseconds < 5000)
            {
                context.Drain();
                Thread.Sleep(1);
            }
            Assert.AreEqual(BrowserState.Ready, presenter.ViewModel.BrowserState);
        }

        [Test]
        public void RefreshRetriesFailedInitialDirectoryAndRecoversFiles()
        {
            var previous = SynchronizationContext.Current;
            var context = new QueueContext();
            {
                SynchronizationContext.SetSynchronizationContext(context);
                try
                {
                    var model = new FailingFirstListing();
                    using (var transfers = new FileTransferPresenter(new PscpOptions()))
                    using (var presenter = new BrowserPresenter("Remote", model, new SessionData(), transfers))
                    {
                        var path = RemoteBrowserModel.NewDirectory("/");
                        presenter.LoadDirectory(path);
                        WaitForListing(presenter, context);
                        Assert.IsNull(presenter.CurrentPath);
                        presenter.Refresh();
                        WaitForListing(presenter, context);
                        Assert.AreEqual(2, model.Requests.Count);
                        Assert.AreSame(path, model.Requests[1]);
                        Assert.AreSame(path, presenter.CurrentPath);
                        Assert.AreEqual("recovered.txt", presenter.ViewModel.Files.Single().Name);
                        presenter.LoadDirectory(null);
                        Assert.AreEqual(BrowserState.Ready, presenter.ViewModel.BrowserState);
                    }
                }
                finally { SynchronizationContext.SetSynchronizationContext(previous); }
            }
        }

        [Test]
        public void RefreshBeforeAnyDirectoryRequestLeavesBrowserReady()
        {
            using (var transfers = new FileTransferPresenter(new PscpOptions()))
            using (var presenter = new BrowserPresenter("Remote", new FailingFirstListing(), new SessionData(), transfers))
            {
                presenter.Refresh();
                Assert.AreEqual(BrowserState.Ready, presenter.ViewModel.BrowserState);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Server sent a diagnostic line")]
        [TestCase("file | 1 kB")]
        [TestCase("file | invalid kB | 2.5 kB/s | ETA: 00:01 | 50%")]
        [TestCase("file | 999999999999 kB | 2.5 kB/s | ETA: 00:01 | 50%")]
        [TestCase("file | 1 kB | unknown kB/s | ETA: 00:01 | 50%")]
        [TestCase("file | 1 kB | NaN kB/s | ETA: 00:01 | 50%")]
        [TestCase("file | 1 kB | 2.5 kB/s | ETA: 00:01 | 101%")]
        public void InvalidProgressOutputIsIgnored(string line)
        {
            FileTransferStatus status;
            Assert.IsFalse(new PscpClient.ScpLineParser().TryParseTransferStatus(line, out status));
        }

        [TestCase("en-US")]
        [TestCase("de-DE")]
        public void ValidProgressUsesInvariantNumbersAndSupportsPipesInFilename(string culture)
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                FileTransferStatus status;
                Assert.IsTrue(new PscpClient.ScpLineParser().TryParseTransferStatus(
                    "file|name.txt | 123 kB | 2.5 kB/s | ETA: 00:01 | 50%", out status));
                Assert.AreEqual("file|name.txt", status.Filename);
                Assert.AreEqual(123, status.BytesTransferred);
                Assert.AreEqual(2.5f, status.TransferRate);
                Assert.AreEqual("00:01", status.TimeLeft);
                Assert.AreEqual(50, status.PercentComplete);
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(10, false)]
        [TestCase(11, false)]
        [TestCase(12, false)]
        [TestCase(11, true)]
        public void ReaderPreservesFinalLineAcrossReadingModes(int precedingLines, bool trailingNewline)
        {
            var expected = Enumerable.Range(0, precedingLines).Select(i => "line " + i).ToList();
            expected.Add("fatal: connection refused");
            string input = String.Join("\n", expected) + (trailingNewline ? "\n" : "");
            var notified = new List<string>();
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(input)))
            using (var text = new StreamReader(stream))
            using (var reader = new PscpClient.AsyncStreamReader("ERR", text,
                line => { notified.Add(line); return true; }))
            {
                CollectionAssert.AreEqual(expected, reader.StopAndGetData());
                CollectionAssert.AreEqual(expected, notified);
            }
        }

        [Test]
        public void PromptNotificationIsNotDuplicatedAtEndOfStream()
        {
            var notified = new List<string>();
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("Password:")))
            using (var text = new StreamReader(stream))
            using (var reader = new PscpClient.AsyncStreamReader("ERR", text,
                line => { notified.Add(line); return true; }))
            {
                CollectionAssert.AreEqual(new[] { "Password:" }, reader.StopAndGetData());
                CollectionAssert.AreEqual(new[] { "Password:" }, notified);
            }
        }
    }
}
