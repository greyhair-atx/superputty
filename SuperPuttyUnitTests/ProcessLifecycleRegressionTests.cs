using System;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Scp;
using Microsoft.CSharp;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class ProcessLifecycleRegressionTests
    {
        [Test]
        public void ResponsiveWindowTitlePreservesUnicode()
        {
            using (var form = new Form { Text = "Terminal – 日本語" })
            {
                string title;
                Assert.IsTrue(ApplicationPanel.TryReadWindowTitle(form.Handle, out title));
                Assert.AreEqual(form.Text, title);
                Assert.IsFalse(ApplicationPanel.TryReadWindowTitle(IntPtr.Zero, out title));
            }
        }

        [Test]
        public void HungWindowTitleReadReturnsWithinBound()
        {
            using (var ready = new ManualResetEventSlim())
            using (var blocked = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                Form form = null;
                IntPtr handle = IntPtr.Zero;
                var thread = new Thread(() =>
                {
                    using (form = new Form { ShowInTaskbar = false, Text = "Hung terminal" })
                    {
                        form.Shown += (s, e) => { handle = form.Handle; ready.Set(); };
                        Application.Run(form);
                    }
                }) { IsBackground = true };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                try
                {
                    Assert.IsTrue(ready.Wait(5000));
                    form.BeginInvoke(new Action(() =>
                    {
                        blocked.Set();
                        // STA wait handles can pump sent messages; sleeping simulates a hung UI.
                        var wait = Stopwatch.StartNew();
                        while (!release.IsSet && wait.ElapsedMilliseconds < 5000)
                            Thread.Sleep(10);
                    }));
                    Assert.IsTrue(blocked.Wait(5000));
                    var elapsed = Stopwatch.StartNew();
                    string title;
                    Assert.IsFalse(ApplicationPanel.TryReadWindowTitle(handle, out title));
                    Assert.Less(elapsed.ElapsedMilliseconds, 1000);
                }
                finally
                {
                    release.Set();
                    if (form != null && form.IsHandleCreated)
                        form.BeginInvoke(new Action(form.Close));
                    Assert.IsTrue(thread.Join(5000));
                }
            }
        }

        private sealed class QueueContext : SynchronizationContext
        {
            private readonly Queue<Action> callbacks = new Queue<Action>();
            public override void Post(SendOrPostCallback callback, object state)
            {
                lock (callbacks) callbacks.Enqueue(() => callback(state));
            }
            internal void Drain()
            {
                while (true)
                {
                    Action callback;
                    lock (callbacks)
                    {
                        if (callbacks.Count == 0) return;
                        callback = callbacks.Dequeue();
                    }
                    callback();
                }
            }
        }

        private sealed class PendingDirectory : ICancellableBrowserModel, IDisposable
        {
            internal readonly ManualResetEventSlim Started = new ManualResetEventSlim();
            internal bool WasCanceled;
            internal ResultStatusCode Result = ResultStatusCode.RetryAuthentication;
            public ListDirectoryResult ListDirectory(SessionData session, BrowserFileInfo path)
            {
                throw new AssertionException("Expected the cancellable overload.");
            }
            public ListDirectoryResult ListDirectory(SessionData session, BrowserFileInfo path, CancellationToken token)
            {
                Started.Set();
                token.WaitHandle.WaitOne(5000);
                WasCanceled = token.IsCancellationRequested;
                return new ListDirectoryResult(path) { StatusCode = Result };
            }
            public void Dispose() { Started.Dispose(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClosingScpPresentersStopsTheirChildProcess(bool transfer)
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPuttyScpLifecycle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string executable = Path.Combine(directory, "FakePscp.exe");
            string marker = Path.ChangeExtension(executable, ".pid");
            var previous = SynchronizationContext.Current;
            var context = new QueueContext();
            Process process = null;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                using (var compiler = new CSharpCodeProvider())
                {
                    var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = executable };
                    options.ReferencedAssemblies.Add("System.dll");
                    var compiled = compiler.CompileAssemblyFromSource(options,
                        "class FakePscp { static void Main() { System.IO.File.WriteAllText(System.IO.Path.ChangeExtension(System.Environment.GetCommandLineArgs()[0], \".pid\"), System.Diagnostics.Process.GetCurrentProcess().Id.ToString()); System.Threading.Thread.Sleep(60000); } }");
                    Assert.IsFalse(compiled.Errors.HasErrors);
                }
                var optionsPscp = new PscpOptions { PscpLocation = executable, TimeoutMs = 30000 };
                var session = new SessionData { Host = "example.invalid", Username = "test", Port = 22 };
                using (var transfers = new FileTransferPresenter(optionsPscp))
                using (var browser = new BrowserPresenter("Remote", new RemoteBrowserModel(optionsPscp), session, transfers))
                using (var tab = transfer ? null : new PscpBrowserPanel(session, optionsPscp, directory))
                {
                    if (transfer)
                    {
                        var request = new FileTransferRequest
                        {
                            Session = session,
                            TargetFile = new BrowserFileInfo { Source = SourceType.Local, Path = directory, Type = FileType.Directory }
                        };
                        request.SourceFiles.Add(new BrowserFileInfo { Source = SourceType.Remote, Path = "/test", Type = FileType.File });
                        transfers.TransferFiles(request);
                    }
                    var elapsed = Stopwatch.StartNew();
                    while (!File.Exists(marker) && elapsed.ElapsedMilliseconds < 5000)
                    {
                        context.Drain();
                        Thread.Sleep(10);
                    }
                    Assert.IsTrue(File.Exists(marker), "Child process did not start.");
                    process = Process.GetProcessById(Int32.Parse(File.ReadAllText(marker)));
                    context.Drain();
                    int updates = 0;
                    transfers.ViewModel.FileTransfers.ListChanged += (s, e) => updates++;
                    browser.Dispose();
                    transfers.Dispose();
                    tab?.Dispose();
                    Assert.IsTrue(process.WaitForExit(5000), "Closing the presenters left PSCP running.");
                    context.Drain();
                    Assert.AreEqual(0, updates, "A late transfer callback updated the disposed presenter.");
                }
            }
            finally
            {
                if (process != null)
                {
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
                    process.Dispose();
                }
                SynchronizationContext.SetSynchronizationContext(previous);
                File.Delete(marker);
                File.Delete(executable);
                Directory.Delete(directory);
            }
        }

        [TestCase(ResultStatusCode.RetryAuthentication)]
        [TestCase(ResultStatusCode.Success)]
        public void ClosingBrowserCancelsRequestAndSuppressesLateAuthenticationAndUpdates(ResultStatusCode result)
        {
            var previous = SynchronizationContext.Current;
            var context = new QueueContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                using (var model = new PendingDirectory { Result = result })
                using (var transfers = new FileTransferPresenter(new PscpOptions()))
                using (var presenter = new BrowserPresenter("Remote", model, new SessionData(), transfers))
                {
                    int authRequests = 0;
                    int updates = 0;
                    presenter.AuthRequest += (s, e) => authRequests++;
                    presenter.LoadDirectory(RemoteBrowserModel.NewDirectory("/"));
                    Assert.IsTrue(model.Started.Wait(5000));
                    context.Drain();
                    presenter.ViewModel.PropertyChanged += (s, e) => updates++;
                    var worker = (BackgroundWorker)typeof(BrowserPresenter).GetProperty("BackgroundWorker",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(presenter);
                    presenter.Dispose();
                    var elapsed = Stopwatch.StartNew();
                    while (worker.IsBusy && elapsed.ElapsedMilliseconds < 5000)
                    {
                        context.Drain();
                        Thread.Sleep(1);
                    }
                    context.Drain();
                    Assert.IsFalse(worker.IsBusy);
                    Assert.IsTrue(model.WasCanceled);
                    Assert.AreEqual(0, authRequests);
                    Assert.AreEqual(0, updates);
                    presenter.Refresh();
                    Assert.IsFalse(worker.IsBusy);
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }
}
