using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SuperPutty;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class UpdateRequestClientTests
    {
        private sealed class ProgressStream : MemoryStream
        {
            internal int Reads;
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            {
                await Task.Delay(15, token);
                Reads++;
                buffer[offset] = 65;
                return 1;
            }
        }

        [Test]
        public void PreCanceledUpdateDoesNotStartItsRequestOrDeadlineOperation()
        {
            Assert.CatchAsync<OperationCanceledException>(() => UpdateRequestClient.DownloadJsonAsync(
                "https://invalid.invalid/releases", new CancellationToken(true)));
            bool started = false, aborted = false;
            Assert.CatchAsync<OperationCanceledException>(() => UpdateRequestClient.WithDeadlineAsync(
                token => { started = true; return Task.FromResult("unexpected"); },
                () => aborted = true, new CancellationToken(true), 100));
            Assert.IsFalse(started);
            Assert.IsFalse(aborted);
        }

        [Test]
        public void OneDeadlineCoversHeadersAndASlowBodyDespiteContinuedProgress()
        {
            using (var stream = new ProgressStream())
            {
                bool aborted = false;
                var elapsed = Stopwatch.StartNew();
                Task<string> request = UpdateRequestClient.WithDeadlineAsync(async token =>
                {
                    await Task.Delay(60, token); // Headers use part of the same request budget.
                    return await UpdateRequestClient.ReadJsonAsync(stream, token);
                }, () => aborted = true, CancellationToken.None, 150);
                Assert.IsFalse(request.IsCompleted, "Network work must yield to its caller.");
                Assert.ThrowsAsync<TimeoutException>(async () => await request);
                Assert.Greater(stream.Reads, 0, "The body should be making progress before the deadline.");
                Assert.IsTrue(aborted, "The deadline must abort the network operation too.");
                Assert.Less(elapsed.ElapsedMilliseconds, 2000);
            }
        }

        [Test]
        public void DeadlineAbortsAnOutstandingHeaderWaitAndRejectsAResultReturnedByAbort()
        {
            var response = new TaskCompletionSource<string>();
            int aborted = 0;
            Task<string> request = UpdateRequestClient.WithDeadlineAsync(token => response.Task,
                () => { Interlocked.Increment(ref aborted); response.SetResult("late result"); },
                CancellationToken.None, 50);
            Assert.ThrowsAsync<TimeoutException>(async () => await request);
            Assert.AreEqual(1, aborted);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DeadlineStopsWaitingEvenWhenNetworkWorkIgnoresCancellationOrAbortThrows(bool throwFromAbort)
        {
            var pending = new TaskCompletionSource<string>();
            bool aborted = false;
            Task<string> request = UpdateRequestClient.WithDeadlineAsync(token => pending.Task, () =>
            {
                aborted = true;
                if (throwFromAbort) throw new IOException("Expected abort failure");
            }, CancellationToken.None, 50);
            Task completed = await Task.WhenAny(request, Task.Delay(2000));
            Assert.AreSame(request, completed, "An uncooperative operation must not extend the deadline.");
            Assert.ThrowsAsync<TimeoutException>(async () => await request);
            Assert.IsTrue(aborted);
            pending.SetException(new IOException("Expected late network fault"));
        }

        [Test]
        public void ExplicitCancellationRemainsCancellationAndAbortsTheRequest()
        {
            using (var stream = new ProgressStream())
            using (var cancellation = new CancellationTokenSource())
            {
                bool aborted = false;
                Task<string> request = UpdateRequestClient.WithDeadlineAsync(
                    token => UpdateRequestClient.ReadJsonAsync(stream, token),
                    () => aborted = true, cancellation.Token, 5000);
                cancellation.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await request);
                Assert.IsTrue(aborted);
            }
        }

        [Test]
        public async Task JsonReaderPreservesUtf8AndAllowsTheExactResponseLimit()
        {
            const string json = "{\"message\":\"caf\u00e9 \u03bb\u6587\"}";
            using (var input = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                Assert.AreEqual(json, await UpdateRequestClient.ReadJsonAsync(input, CancellationToken.None));
            using (var input = new MemoryStream(new byte[UpdateRequestClient.MaximumResponseBytes]))
                Assert.AreEqual(UpdateRequestClient.MaximumResponseBytes,
                    (await UpdateRequestClient.ReadJsonAsync(input, CancellationToken.None)).Length);
        }

        [Test]
        public void BodyReaderRejectsOversizedDecompressedResponsesEvenWithoutAContentLength()
        {
            using (var compressed = new MemoryStream())
            {
                using (var gzip = new GZipStream(compressed, CompressionMode.Compress, true))
                {
                    byte[] oversized = new byte[UpdateRequestClient.MaximumResponseBytes + 1];
                    gzip.Write(oversized, 0, oversized.Length);
                }
                Assert.Less(compressed.Length, UpdateRequestClient.MaximumResponseBytes);
                compressed.Position = 0;
                using (var body = new GZipStream(compressed, CompressionMode.Decompress))
                    Assert.ThrowsAsync<InvalidDataException>(() => UpdateRequestClient.ReadJsonAsync(body, CancellationToken.None));
            }
        }

        [TestCase(HttpStatusCode.Redirect, "application/json", -1L, "https://example.com/releases")]
        [TestCase(HttpStatusCode.InternalServerError, "application/json", -1L, "https://example.com/releases")]
        [TestCase(HttpStatusCode.OK, "text/html", -1L, "https://example.com/releases")]
        [TestCase(HttpStatusCode.OK, null, -1L, "https://example.com/releases")]
        [TestCase(HttpStatusCode.OK, "application/json", 1048577L, "https://example.com/releases")]
        [TestCase(HttpStatusCode.OK, "application/json", -1L, "http://example.com/releases")]
        [TestCase(HttpStatusCode.OK, "application/json", -1L, "https://user:secret@example.com/releases")]
        public void ResponseValidationRejectsRedirectsErrorsWrongTypeOversizeAndInsecureUris(
            HttpStatusCode status, string contentType, long length, string uri)
        {
            Assert.Throws<InvalidDataException>(() => UpdateRequestClient.ValidateResponse(status, contentType, length, new Uri(uri)));
        }

        [Test]
        public void UnknownLengthJsonResponseStillUsesBodyEnforcement()
        {
            Assert.DoesNotThrow(() => UpdateRequestClient.ValidateResponse(HttpStatusCode.OK,
                "application/json; charset=utf-8", -1, new Uri("https://example.com/releases")));
        }
    }
}
