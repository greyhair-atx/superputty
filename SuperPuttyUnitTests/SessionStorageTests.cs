using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SuperPutty.Data;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class SessionStorageTests
    {
        private static TaskCompletionSource<List<SessionData>> PendingRead()
        {
            return new TaskCompletionSource<List<SessionData>>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task<T> CompletesPromptly<T>(Task<T> operation)
        {
            if (await Task.WhenAny(operation, Task.Delay(TimeSpan.FromSeconds(3))) != operation)
                Assert.Fail("The session operation did not complete after cancellation or its deadline.");
            return await operation;
        }

        [Test]
        public async Task PendingRemoteCollectionYieldsUntilTheReadCompletes()
        {
            var pending = PendingRead();
            var storage = new SessionStorage(readRemote: (uri, token) => pending.Task);
            var load = storage.LoadAsync("https://example.invalid/root.xml", CancellationToken.None);

            Assert.False(load.IsCompleted);
            pending.SetResult(new List<SessionData> { new SessionData { SessionId = "Server" } });

            var result = await CompletesPromptly(load);
            Assert.AreEqual("Server", result.Single().SessionId);
        }

        [Test]
        public void CancellationStopsWaitingForAReaderThatDoesNotHonorItsToken()
        {
            var pending = PendingRead();
            var storage = new SessionStorage(readRemote: (uri, token) => pending.Task);
            using (var cancellation = new CancellationTokenSource())
            {
                var load = storage.LoadAsync("https://example.invalid/root.xml", cancellation.Token);
                cancellation.Cancel();

                Assert.ThrowsAsync<OperationCanceledException>(async () => await CompletesPromptly(load));
                pending.SetResult(new List<SessionData> { new SessionData { SessionId = "TooLate" } });
                Assert.True(load.IsCanceled);
            }
        }

        [Test]
        public void LocalReadThatDoesNotHonorItsTokenStillHasAnOverallDeadline()
        {
            var pending = PendingRead();
            var storage = new SessionStorage(readLocal: (path, token) => pending.Task,
                operationTimeout: TimeSpan.FromMilliseconds(40));
            var load = storage.LoadAsync(Path.Combine(Path.GetTempPath(), "Sessions.xml"), CancellationToken.None);

            Assert.ThrowsAsync<TimeoutException>(async () => await CompletesPromptly(load));
            pending.SetException(new IOException("A late filesystem failure must not replace the timeout."));
            Assert.True(load.IsFaulted);
            Assert.IsInstanceOf<TimeoutException>(load.Exception.InnerException);
        }

        [Test]
        public void OverallDeadlineIsSharedByNestedCollectionReads()
        {
            var storage = new SessionStorage(readRemote: async (uri, token) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(125), token);
                return uri.AbsolutePath == "/root.xml"
                    ? new List<SessionData> { new SessionData { CollectionLocation = "child.xml" } }
                    : new List<SessionData> { new SessionData { SessionId = "Server" } };
            }, operationTimeout: TimeSpan.FromMilliseconds(200));

            Assert.ThrowsAsync<TimeoutException>(async () => await CompletesPromptly(
                storage.LoadAsync("https://example.invalid/root.xml", CancellationToken.None)));
        }

        [Test]
        public async Task NestedRemoteCollectionsKeepPrefixesAndResolveRelativeLocations()
        {
            var readLocations = new List<string>();
            var storage = new SessionStorage(readRemote: (uri, token) =>
            {
                readLocations.Add(uri.AbsoluteUri);
                List<SessionData> sessions;
                switch (uri.AbsolutePath)
                {
                    case "/root.xml":
                        sessions = new List<SessionData>
                        {
                            new SessionData { CollectionID = "/Outer/", CollectionLocation = "children/child.xml" }
                        };
                        break;
                    case "/children/child.xml":
                        sessions = new List<SessionData>
                        {
                            new SessionData { CollectionID = "/Inner/", CollectionLocation = "../leaf.xml" }
                        };
                        break;
                    default:
                        sessions = new List<SessionData>
                        {
                            new SessionData { SessionId = "Group/Server", SPSLFileName = "scripts/login.spsl" }
                        };
                        break;
                }
                return Task.FromResult(sessions);
            });

            var result = await storage.LoadAsync("https://example.invalid/root.xml", CancellationToken.None);

            Assert.AreEqual("Outer/Inner/Group/Server", result.Single().SessionId);
            Assert.AreEqual("https://example.invalid/scripts/login.spsl", result.Single().SPSLFileName);
            CollectionAssert.AreEqual(new[]
            {
                "https://example.invalid/root.xml", "https://example.invalid/children/child.xml", "https://example.invalid/leaf.xml"
            }, readLocations);
        }

        [Test]
        public void CyclicCollectionFailsBeforeReadingTheSameActiveCollectionAgain()
        {
            int reads = 0;
            var storage = new SessionStorage(readRemote: (uri, token) =>
            {
                reads++;
                return Task.FromResult(new List<SessionData>
                {
                    new SessionData { CollectionLocation = "root.xml" }
                });
            });

            Assert.ThrowsAsync<InvalidDataException>(async () => await storage.LoadAsync(
                "https://example.invalid/root.xml", CancellationToken.None));
            Assert.AreEqual(1, reads);
        }

        [Test]
        public void BroadCollectionExpansionHasATotalCollectionLimit()
        {
            int reads = 0;
            var storage = new SessionStorage(readRemote: (uri, token) =>
            {
                reads++;
                return Task.FromResult(uri.AbsolutePath == "/root.xml"
                    ? Enumerable.Range(0, SessionStorage.MaximumCollections).Select(index =>
                        new SessionData { CollectionLocation = "child" + index + ".xml" }).ToList()
                    : new List<SessionData>());
            });

            Assert.ThrowsAsync<InvalidDataException>(async () => await storage.LoadAsync(
                "https://example.invalid/root.xml", CancellationToken.None));
            Assert.AreEqual(SessionStorage.MaximumCollections, reads);
        }

        [Test]
        public void DeepCollectionExpansionHasANestingLimit()
        {
            var storage = new SessionStorage(readRemote: (uri, token) =>
            {
                int depth = Int32.Parse(Path.GetFileNameWithoutExtension(uri.AbsolutePath));
                return Task.FromResult(new List<SessionData>
                {
                    new SessionData { CollectionLocation = (depth + 1) + ".xml" }
                });
            });

            Assert.ThrowsAsync<InvalidDataException>(async () => await storage.LoadAsync(
                "https://example.invalid/0.xml", CancellationToken.None));
        }

        [Test]
        public void ExpandedCatalogHasATotalSessionLimit()
        {
            var sessions = Enumerable.Repeat(new SessionData { SessionId = "Repeated" },
                SessionStorage.MaximumSessions + 1).ToList();
            var storage = new SessionStorage(readRemote: (uri, token) => Task.FromResult(sessions));

            Assert.ThrowsAsync<InvalidDataException>(async () => await storage.LoadAsync(
                "https://example.invalid/root.xml", CancellationToken.None));
        }

        [Test]
        public void FailedNestedCollectionDoesNotReturnAPartialCatalog()
        {
            var storage = new SessionStorage(readRemote: (uri, token) => uri.AbsolutePath == "/root.xml"
                ? Task.FromResult(new List<SessionData>
                {
                    new SessionData { SessionId = "ValidFirstEntry" },
                    new SessionData { CollectionLocation = "unavailable.xml" }
                })
                : Task.FromException<List<SessionData>>(new IOException("The nested collection is unavailable.")));

            var error = Assert.ThrowsAsync<IOException>(async () => await storage.LoadAsync(
                "https://example.invalid/root.xml", CancellationToken.None));
            StringAssert.Contains("nested collection", error.Message);
        }

        [Test]
        public void NullRemoteEntryIsRejectedAsInvalidDataBeforeResolvingLocations()
        {
            var storage = new SessionStorage(readRemote: (uri, token) =>
                Task.FromResult(new List<SessionData> { null }));

            Assert.ThrowsAsync<InvalidDataException>(async () => await storage.LoadAsync(
                "https://example.invalid/root.xml", CancellationToken.None));
        }

        [Test]
        public void UnrelatedReaderCancellationIsNotMisreportedAsATimeout()
        {
            var canceled = new CancellationToken(true);
            var storage = new SessionStorage(readRemote: (uri, token) =>
                Task.FromCanceled<List<SessionData>>(canceled));

            Assert.ThrowsAsync<TaskCanceledException>(async () => await storage.LoadAsync(
                "https://example.invalid/root.xml", CancellationToken.None));
        }
    }
}
