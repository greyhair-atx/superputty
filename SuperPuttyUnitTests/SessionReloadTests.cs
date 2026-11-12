using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SuperPutty.Data;
using App = SuperPutty.SuperPuTTY;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class SessionReloadTests
    {
        private sealed class PendingStorage : ISessionStorage
        {
            internal readonly TaskCompletionSource<List<SessionData>> Result = new TaskCompletionSource<List<SessionData>>();
            public Task<List<SessionData>> LoadAsync(string path, CancellationToken token) { return Result.Task; }
            public void Save(List<SessionData> sessions, string path) { Assert.Fail("Reload must not save"); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CanceledOrStaleReloadPreservesCurrentCatalogAndFile(bool changeCatalog)
        {
            string originalFolder = App.Settings.SettingsFolder;
            var original = App.GetAllSessions();
            string directory = Path.Combine(Path.GetTempPath(), "SuperPuttyReload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "Sessions.XML");
            File.WriteAllText(file, "original bytes");
            using (var cancellation = new CancellationTokenSource())
            try
            {
                App.Settings.SettingsFolder = directory;
                var current = new SessionData { SessionId = "Current", SessionName = "Current" };
                App.ReplaceSessionCatalog(new[] { current });
                var storage = new PendingStorage();
                Task load = App.LoadSessionsAsync(cancellation.Token, storage);
                Assert.IsFalse(load.IsCompleted);
                if (changeCatalog) App.AddSession(new SessionData { SessionId = "Added", SessionName = "Added" });
                else cancellation.Cancel();
                storage.Result.SetResult(new List<SessionData> { new SessionData { SessionId = "Replacement", SessionName = "Replacement" } });
                if (changeCatalog) Assert.ThrowsAsync<InvalidOperationException>(async () => await load);
                else Assert.ThrowsAsync<OperationCanceledException>(async () => await load);
                Assert.AreSame(current, App.GetSessionById("Current"));
                Assert.IsNull(App.GetSessionById("Replacement"));
                Assert.AreEqual("original bytes", File.ReadAllText(file));
            }
            finally
            {
                App.ReplaceSessionCatalog(original);
                App.Settings.SettingsFolder = originalFolder;
                Directory.Delete(directory, true);
            }
        }
    }
}
