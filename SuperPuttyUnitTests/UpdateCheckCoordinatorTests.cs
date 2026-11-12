using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class UpdateCheckCoordinatorTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task SupersededRequestCannotPublishLateSuccessOrFailure(bool failOldRequest)
        {
            using (var checks = new UpdateCheckCoordinator())
            {
                var pending = new TaskCompletionSource<string>();
                CancellationToken oldToken = default(CancellationToken);
                var published = new List<string>();
                Task old = checks.RunAsync(token => { oldToken = token; return pending.Task; },
                    (success, content) => published.Add("stale"));
                await checks.RunAsync(token => Task.FromResult("latest"),
                    (success, content) => { Assert.True(success); published.Add(content); });
                Assert.True(oldToken.IsCancellationRequested);
                if (failOldRequest) pending.SetException(new IOException("Old failure"));
                else pending.SetResult("Old success");
                await old;
                CollectionAssert.AreEqual(new[] { "latest" }, published);
                Assert.False(checks.IsRunning);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancelingExitPreservesWorkAndConfirmedExitCancelsIt(bool cancelExit)
        {
            using (var checks = new UpdateCheckCoordinator())
            {
                var pending = new TaskCompletionSource<string>();
                CancellationToken observed = default(CancellationToken);
                int published = 0;
                Task running = checks.RunAsync(token => { observed = token; return pending.Task; },
                    (success, content) => published++);
                checks.OnClosing(cancelExit);
                Assert.AreEqual(!cancelExit, observed.IsCancellationRequested);
                pending.SetResult("response");
                await running;
                Assert.AreEqual(cancelExit ? 1 : 0, published);
            }
        }

        [Test]
        public async Task ComponentContainerDisposalCancelsWorkAndSuppressesPublication()
        {
            var checks = new UpdateCheckCoordinator();
            var pending = new TaskCompletionSource<string>();
            CancellationToken observed = default(CancellationToken);
            Task running;
            using (var components = new Container())
            {
                components.Add(checks);
                running = checks.RunAsync(token => { observed = token; return pending.Task; },
                    (success, content) => Assert.Fail("Disposed UI received an update result."));
            }
            Assert.True(observed.IsCancellationRequested);
            pending.SetResult("late");
            await running;
            Assert.False(checks.IsRunning);
        }

        [Test]
        public async Task ActiveFailureIsPublishedOnce()
        {
            using (var checks = new UpdateCheckCoordinator())
            {
                int published = 0;
                await checks.RunAsync(token => Task.FromException<string>(new IOException("Expected failure")),
                    (success, content) =>
                    {
                        published++;
                        Assert.False(success);
                        StringAssert.Contains("Expected failure", content);
                    });
                Assert.AreEqual(1, published);
                Assert.False(checks.IsRunning);
            }
        }

        [Test]
        public async Task CompletingOldRequestCannotClearANewerRunningRequest()
        {
            using (var checks = new UpdateCheckCoordinator())
            {
                var oldResult = new TaskCompletionSource<string>();
                var newResult = new TaskCompletionSource<string>();
                Task old = checks.RunAsync(token => oldResult.Task, (success, content) => Assert.Fail("Stale response"));
                Task current = checks.RunAsync(token => newResult.Task, (success, content) => { });
                oldResult.SetResult("old");
                await old;
                Assert.True(checks.IsRunning);
                newResult.SetResult("new");
                await current;
                Assert.False(checks.IsRunning);
            }
        }

        [Test]
        public async Task DisposedCoordinatorDoesNotStartNewNetworkWork()
        {
            var checks = new UpdateCheckCoordinator();
            checks.Dispose();
            await checks.RunAsync(token => { Assert.Fail("Disposed coordinator started work"); return Task.FromResult(""); },
                (success, content) => Assert.Fail("Disposed coordinator published"));
        }

        [Test]
        public async Task ThrowingCancellationCallbackCannotPreventTheReplacementCheck()
        {
            using (var checks = new UpdateCheckCoordinator())
            {
                var pending = new TaskCompletionSource<string>();
                CancellationTokenRegistration registration = default(CancellationTokenRegistration);
                Task old = checks.RunAsync(token =>
                {
                    registration = token.Register(() => { throw new IOException("Cancellation callback failed"); });
                    return pending.Task;
                }, (success, content) => Assert.Fail("Superseded request published"));
                try
                {
                    int publications = 0;
                    await checks.RunAsync(token => Task.FromResult("new"), (success, content) => publications++);
                    pending.SetResult("old");
                    await old;
                    Assert.AreEqual(1, publications);
                    Assert.False(checks.IsRunning);
                }
                finally { registration.Dispose(); }
            }
        }
    }
}
