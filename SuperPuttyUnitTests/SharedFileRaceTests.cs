using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.CSharp;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable]
    public class SharedFileRaceTests
    {
        private string directory;
        private string worker;
        private string PathFor(string name) { return Path.Combine(directory, name); }

        [SetUp]
        public void SetUp()
        {
            worker = null;
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-FileRace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown() { Directory.Delete(directory, true); }

        private sealed class Provider : PortableSettingsProvider
        {
            internal string Folder;
            public override string[] GetAppSettingsPaths() { return new[] { Folder }; }
            public override string GetAppSettingsFileName() { return "test.settings"; }
        }

        private static void Set(Provider provider, string value)
        {
            var property = new SettingsProperty("Value") { PropertyType = typeof(string), Provider = provider,
                SerializeAs = SettingsSerializeAs.String, DefaultValue = "" };
            var values = new SettingsPropertyValueCollection
            {
                new SettingsPropertyValue(property) { SerializedValue = value }
            };
            provider.SetPropertyValues(new SettingsContext(), values);
        }

        [Test]
        public void OversizedPreferencesRecoverTheBackupAndPreserveTheOriginal()
        {
            string path = PathFor("test.settings");
            long length = PortableSettingsProvider.MaximumSettingsBytes + 1L;
            using (var stream = File.Create(path)) stream.SetLength(length);
            File.WriteAllText(path + ".bak", "<Settings><Value>recovered</Value></Settings>");
            var provider = new Provider { Folder = directory };

            Assert.AreEqual("recovered", provider.SettingsXML.SelectSingleNode("/Settings/Value").InnerText);
            Assert.AreEqual(length, new FileInfo(provider.RecoveryNotification.PreservedFilePath).Length);
        }

        [Test]
        public void OversizedSessionCatalogIsRejectedBeforeDeserializationOrBackup()
        {
            string path = PathFor("Sessions.XML");
            long length = SessionStorage.MaximumLocalCatalogBytes + 1L;
            using (var stream = File.Create(path)) stream.SetLength(length);

            Assert.ThrowsAsync<InvalidDataException>(async () => await new SessionStorage().LoadAsync(path, CancellationToken.None));
            Assert.Throws<InvalidDataException>(() => new SessionStorage().Save(new List<SessionData>(), path));
            Assert.AreEqual(length, new FileInfo(path).Length);
            Assert.AreEqual(1, Directory.GetFiles(directory).Length);
        }

        [Test]
        public void PreferencesRejectStaleSaveWithoutChangingPrimaryOrBackup()
        {
            string path = PathFor("test.settings");
            File.WriteAllText(path, "<Settings><Value>original</Value></Settings>");
            var first = new Provider { Folder = directory };
            var second = new Provider { Folder = directory };
            Assert.IsNotNull(first.SettingsXML);
            Assert.IsNotNull(second.SettingsXML);
            Set(first, "first");
            byte[] primary = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");
            var error = Assert.Throws<ConfigurationErrorsException>(() => Set(second, "stale"));
            StringAssert.Contains("Reload", error.Message);
            CollectionAssert.AreEqual(primary, File.ReadAllBytes(path));
            CollectionAssert.AreEqual(backup, File.ReadAllBytes(path + ".bak"));
            Assert.AreEqual("original", second.SettingsXML.SelectSingleNode("/Settings/Value").InnerText);
            Set(first, "first again");
            Set(new Provider { Folder = directory }, "reloaded");
        }

        [Test]
        public void SessionCatalogRejectsStaleSaveAndAllowsExplicitReload()
        {
            string path = PathFor("Sessions.xml");
            new SessionStorage().Save(new List<SessionData> { new SessionData { SessionId = "server", Host = "original" } }, path);
            var first = new SessionStorage();
            var second = new SessionStorage();
            var a = first.LoadAsync(path, CancellationToken.None).GetAwaiter().GetResult();
            var b = second.LoadAsync(path, CancellationToken.None).GetAwaiter().GetResult();
            a[0].Host = "first";
            first.Save(a, path);
            byte[] saved = File.ReadAllBytes(path);
            b[0].Host = "stale";
            Assert.Throws<SharedFile.ChangedException>(() => second.Save(b, path));
            CollectionAssert.AreEqual(saved, File.ReadAllBytes(path));
            b = second.LoadAsync(path, CancellationToken.None).GetAwaiter().GetResult();
            b[0].Host = "reloaded";
            Assert.DoesNotThrow(() => second.Save(b, path));
        }

        [Test]
        public void AFileCreatedAfterMissingCatalogWasObservedIsNotOverwritten()
        {
            string path = PathFor("Sessions.xml");
            var first = new SessionStorage();
            var second = new SessionStorage();
            first.RememberMissing(path);
            second.RememberMissing(path);
            first.Save(new List<SessionData>(), path);
            Assert.Throws<SharedFile.ChangedException>(() => second.Save(new List<SessionData>(), path));
        }

        [Test]
        public void LayoutRecoveryKeepsAValidPrimaryPublishedDuringBackupValidation()
        {
            string path = PathFor("layout.xml");
            const string newer = "<DockPanel Generation='new'><Contents Count='0'/></DockPanel>";
            File.WriteAllText(path, "<broken");
            File.WriteAllText(path + ".bak", "<DockPanel Generation='old'><Contents Count='0'/></DockPanel>");
            int calls = 0;
            var result = LayoutStorage.LoadValidated(path, bytes => { if (++calls == 1) File.WriteAllText(path, newer); });
            Assert.AreEqual(newer, Encoding.UTF8.GetString(result.Content));
            Assert.AreEqual(newer, File.ReadAllText(path));
            Assert.IsNull(result.BackupFilePath);
            Assert.IsEmpty(Directory.GetFiles(directory, "*.corrupt.*"));
        }

        [Test]
        public void ConcurrentPreferenceRecoveryPreservesTheDamagedFileOnlyOnce()
        {
            string path = PathFor("test.settings");
            File.WriteAllText(path, "<broken");
            File.WriteAllText(path + ".bak", "<Settings><Value>backup</Value></Settings>");
            var providers = new[] { new Provider { Folder = directory }, new Provider { Folder = directory } };
            Task.WaitAll(providers.Select(provider => Task.Run(() =>
                Assert.AreEqual("backup", provider.SettingsXML.SelectSingleNode("/Settings/Value").InnerText))).ToArray());
            Assert.AreEqual(1, Directory.GetFiles(directory, "*.corrupt.*").Length);
        }

        [Test]
        public void SnapshotReaderAllowsAtomicReplacementAndRetainsItsOriginalBytes()
        {
            string path = PathFor("file.xml"), temporary = PathFor("staged.xml");
            File.WriteAllText(path, "old");
            string revision = SharedFile.Revision(path);
            using (var reader = SharedFile.OpenRead(path))
            {
                File.WriteAllText(temporary, "new");
                SharedFile.Publish(temporary, path, null, revision);
                using (var text = new StreamReader(reader)) Assert.AreEqual("old", text.ReadToEnd());
                Assert.AreEqual("new", File.ReadAllText(path));
            }
        }

        [Test]
        public void BoundedSnapshotRejectsOversizedFileBeforeReadingOrHashingItsContent()
        {
            string path = PathFor("oversized.xml");
            using (var stream = File.Create(path)) stream.SetLength(LayoutStorage.MaximumLayoutBytes + 1L);
            SharedFile.FileVersion version = null;

            Assert.Throws<InvalidDataException>(() =>
                SharedFile.Capture(path, LayoutStorage.MaximumLayoutBytes, out version));

            Assert.IsNotNull(version);
            Assert.False(version.Missing);
            Assert.IsNull(version.Revision, "An oversized snapshot must not hash the entire file before rejecting it.");
        }

        [Test]
        public void BoundedSnapshotAndStreamingRevisionIdentifyTheExactBytes()
        {
            string path = PathFor("snapshot.xml");
            byte[] bytes = Encoding.UTF8.GetBytes("<value>\u03b1\u03b2\u03b3</value>");
            File.WriteAllBytes(path, bytes);
            SharedFile.FileVersion version;

            byte[] snapshot = SharedFile.Capture(path, bytes.Length, out version);

            CollectionAssert.AreEqual(bytes, snapshot);
            Assert.AreEqual(SharedFile.Hash(bytes), version.Revision);
            Assert.AreEqual(version.Revision, SharedFile.Revision(path));
        }

        [Test]
        public void StagedWriteRevisionIncludesBufferedUnicodeSerialization()
        {
            string path = PathFor("staged.xml");
            string value = new string('\u03bb', 10000);
            string revision = SharedFile.WriteStaged(path, stream =>
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true)) writer.Write(value);
            });

            Assert.AreEqual(value, File.ReadAllText(path));
            Assert.AreEqual(SharedFile.Revision(path), revision);
        }

        [Test]
        public void NestedRetryDoesNotRestartAnExpiredOperationDeadline()
        {
            int attempts = 0;
            using (SharedFile.Lock(PathFor("deadline.xml")))
            {
                Thread.Sleep(SharedFile.WaitMilliseconds + 25);
                Assert.Throws<IOException>(() => SharedFile.Retry<int>(() =>
                {
                    attempts++;
                    throw new IOException("Sharing violation", unchecked((int)0x80070020));
                }));
            }
            Assert.AreEqual(1, attempts, "A nested retry must use the original operation's remaining budget.");
        }

        [Test]
        public void OversizedRecoveryKeepsANewValidPrimaryPublishedDuringBackupValidation()
        {
            string path = PathFor("layout.xml");
            const string newer = "<DockPanel Generation='new'><Contents Count='0'/></DockPanel>";
            using (var stream = File.Create(path)) stream.SetLength(LayoutStorage.MaximumLayoutBytes + 1L);
            File.WriteAllText(path + ".bak", "<DockPanel Generation='old'><Contents Count='0'/></DockPanel>");
            int calls = 0;

            var result = LayoutStorage.LoadValidated(path, bytes => { if (++calls == 1) File.WriteAllText(path, newer); });

            Assert.AreEqual(newer, Encoding.UTF8.GetString(result.Content));
            Assert.AreEqual(newer, File.ReadAllText(path));
            Assert.IsNull(result.BackupFilePath);
            Assert.IsEmpty(Directory.GetFiles(directory, "*.corrupt.*"));
        }

        [Test]
        public void TransientExclusiveReaderIsRetriedUntilItCloses()
        {
            string path = PathFor("file.xml");
            File.WriteAllText(path, "ready");
            using (var held = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var holder = Task.Run(() =>
                {
                    using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    { held.Set(); release.Wait(5000); }
                });
                Assert.IsTrue(held.Wait(2000));
                var read = Task.Run(() => SharedFile.Read(path));
                try
                {
                    Assert.IsFalse(read.Wait(100));
                    release.Set();
                    Assert.IsTrue(read.Wait(3000));
                    Assert.AreEqual("ready", Encoding.UTF8.GetString(read.Result));
                }
                finally { release.Set(); holder.GetAwaiter().GetResult(); }
            }
        }

        [Test]
        public void AtomicPublicationWaitsForALegacyReaderToClose()
        {
            string path = PathFor("file.xml"), temporary = PathFor("staged.xml");
            File.WriteAllText(path, "old");
            File.WriteAllText(temporary, "new");
            string revision = SharedFile.Revision(path);
            using (var held = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var holder = Task.Run(() =>
                {
                    using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    { held.Set(); release.Wait(5000); }
                });
                Assert.IsTrue(held.Wait(2000));
                var publish = Task.Run(() => SharedFile.Publish(temporary, path, null, revision));
                try
                {
                    Assert.IsFalse(publish.Wait(100));
                    release.Set();
                    Assert.IsTrue(publish.Wait(3000));
                    Assert.AreEqual("new", File.ReadAllText(path));
                }
                finally { release.Set(); holder.GetAwaiter().GetResult(); }
            }
        }

        [Test]
        public void PersistentSharingFailureHasABoundedWaitAndKeepsTheFile()
        {
            string path = PathFor("file.xml");
            File.WriteAllText(path, "unchanged");
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var elapsed = Stopwatch.StartNew();
                Assert.Throws<IOException>(() => SharedFile.Read(path));
                Assert.Less(elapsed.ElapsedMilliseconds, 5000);
            }
            Assert.AreEqual("unchanged", File.ReadAllText(path));
        }

        private void BuildWorker()
        {
            worker = PathFor("Worker.exe");
            using (var compiler = new CSharpCodeProvider())
            {
                var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = worker };
                options.ReferencedAssemblies.Add("System.dll");
                var result = compiler.CompileAssemblyFromSource(options, @"
using System; using System.IO; using System.Reflection; using System.Threading;
class Worker {
 static void Main(string[] args) {
  var app=Assembly.LoadFrom(args[0]); IDisposable lease=null;
  try {
   string value=""ready"";
   if(args[1]==""lock"") lease=(IDisposable)app.GetType(""SuperPutty.Utils.SharedFile"").GetMethod(""Lock"",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{args[2]});
   else value=(string)app.GetType(""SuperPutty.Utils.RDPStartInfo"").GetMethod(""GetSmartSizingConnectionFile"",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
   File.WriteAllText(args[3]+"".tmp"",value); File.Move(args[3]+"".tmp"",args[3]);
   for(int i=0;i<1500 && !File.Exists(args[4]);i++) Thread.Sleep(10);
  } finally { if(lease!=null) lease.Dispose(); }
 }
}");
                Assert.IsFalse(result.Errors.HasErrors, String.Join("\n", result.Errors.Cast<CompilerError>()));
            }
        }

        private Process StartWorker(string mode, string path, string ready, string release)
        {
            if (worker == null) BuildWorker();
            var arguments = new[] { typeof(SharedFile).Assembly.Location, mode, path, ready, release };
            return Process.Start(new ProcessStartInfo(worker, String.Join(" ", arguments.Select(CommandLineOptions.QuoteArgument)))
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }

        private static void WaitReady(Process process, string ready)
        {
            var elapsed = Stopwatch.StartNew();
            while (!File.Exists(ready) && !process.HasExited && elapsed.ElapsedMilliseconds < 5000) Thread.Sleep(10);
            Assert.IsTrue(File.Exists(ready), "The helper did not become ready.");
        }

        [TestCase("lock")]
        [TestCase("rdp")]
        public void CoordinationAndRdpIsolationWorkAcrossRealProcesses(string mode)
        {
            string path = PathFor("shared.xml"), ready = PathFor("ready"), release = PathFor("release");
            using (var process = StartWorker(mode, path, ready, release))
            {
                try
                {
                    WaitReady(process, ready);
                    if (mode == "lock")
                    {
                        Assert.Throws<IOException>(() => { using (SharedFile.Lock(path)) { } });
                        File.WriteAllText(release, "release");
                        Assert.IsTrue(process.WaitForExit(5000));
                        using (SharedFile.Lock(path)) { }
                    }
                    else
                    {
                        string foreign = File.ReadAllText(ready);
                        var start = new RDPStartInfo(new SessionData { Host = "test" }, "mstsc.exe");
                        string own = start.Args.Split('"')[1];
                        Assert.AreNotEqual(own, foreign);
                        Assert.AreEqual(File.ReadAllText(own), File.ReadAllText(foreign));
                    }
                }
                finally
                {
                    File.WriteAllText(release, "release");
                    if (!process.WaitForExit(5000)) { process.Kill(); process.WaitForExit(); }
                }
            }
        }
    }
}
