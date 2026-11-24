using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.CSharp;
using NUnit.Framework;
using SuperPutty.Utils;
using SuperPutty.Data;
using App = SuperPutty.SuperPuTTY;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable]
    public class SingleInstanceTransportTests
    {
        [Test]
        public async Task RealPipeReportsReadinessAndBoundedUiAdmission()
        {
            string pipeName = "SuperPuttyAdmissionTest-" + Guid.NewGuid().ToString("N");
            var posted = new ConcurrentQueue<Action>();
            string[] observed = null;
            int ready = 0;
            using (var queue = new SingleInstanceCommandQueue(posted.Enqueue,
                args => observed = args, () => Volatile.Read(ref ready) != 0, 1))
            using (var server = new SingleInstancePipeServer(pipeName, queue.TryEnqueue))
            {
                var owner = SingleInstanceEndpoint.Current.User;
                var arguments = new[] { "-session", "Folder/\u03bb session", "", "quoted \"value\"\\" };
                Assert.AreEqual(SingleInstanceReply.NotReady,
                    await SingleInstancePipeClient.SendAsync(pipeName, owner, arguments, CancellationToken.None));
                Volatile.Write(ref ready, 1);
                Assert.AreEqual(SingleInstanceReply.Accepted,
                    await SingleInstancePipeClient.SendAsync(pipeName, owner, arguments, CancellationToken.None));
                Assert.AreEqual(SingleInstanceReply.Busy,
                    await SingleInstancePipeClient.SendAsync(pipeName, owner, arguments, CancellationToken.None));
                Assert.AreEqual(1, queue.PendingCount);
                Action execute;
                Assert.IsTrue(posted.TryDequeue(out execute));
                execute();
                CollectionAssert.AreEqual(arguments, observed);
                Assert.AreEqual(0, queue.PendingCount);
                queue.Stop();
                Assert.AreEqual(SingleInstanceReply.NotReady,
                    await SingleInstancePipeClient.SendAsync(pipeName, owner, arguments, CancellationToken.None));
                server.Dispose();
                Assert.AreSame(server.Completion, await Task.WhenAny(server.Completion, Task.Delay(3000)),
                    "Stopping the listener left a blocked pipe worker.");
                await server.Completion;
            }
        }

        [Test]
        public async Task ReplyRemainsAvailableUntilTheClientReadsIt()
        {
            string pipeName = "SuperPuttyReplyTest-" + Guid.NewGuid().ToString("N");
            var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var server = new SingleInstancePipeServer(pipeName, args =>
                { accepted.TrySetResult(true); return SingleInstanceReply.Accepted; }))
            using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await client.ConnectAsync(3000);
                await SingleInstancePipeProtocol.WriteRequestAsync(client, new[] { "-layout", "delayed reader" }, CancellationToken.None);
                Assert.AreSame(accepted.Task, await Task.WhenAny(accepted.Task, Task.Delay(3000)));
                await Task.Delay(100);
                Assert.AreEqual(SingleInstanceReply.Accepted,
                    await SingleInstancePipeProtocol.ReadReplyAsync(client, CancellationToken.None));
            }
        }

        [Test]
        public async Task StalledClientCannotKeepTheRealListenerOccupied()
        {
            string pipeName = "SuperPuttyDeadlineTest-" + Guid.NewGuid().ToString("N");
            using (var server = new SingleInstancePipeServer(pipeName, args => SingleInstanceReply.Accepted,
                TimeSpan.FromMilliseconds(200)))
            {
                using (var stalled = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    await stalled.ConnectAsync(3000);
                    // Do not send a frame: the listener must close this client and
                    // accept a subsequent request without waiting for this handle.
                    var elapsed = Stopwatch.StartNew();
                    Assert.AreEqual(SingleInstanceReply.Accepted,
                        await SingleInstancePipeClient.SendAsync(pipeName, SingleInstanceEndpoint.Current.User,
                            new[] { "-layout", "after stalled client" }, CancellationToken.None));
                    Assert.Less(elapsed.ElapsedMilliseconds, 3000);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BusyConnectionHonorsTheTotalDeadlineAndCallerCancellation(bool cancel)
        {
            string pipeName = "SuperPuttyBusyDeadlineTest-" + Guid.NewGuid().ToString("N");
            int admitted = 0;
            using (var server = new SingleInstancePipeServer(pipeName, args =>
                { Interlocked.Increment(ref admitted); return SingleInstanceReply.Accepted; }))
            using (var stalled = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            using (var cancellation = new CancellationTokenSource())
            {
                await stalled.ConnectAsync(3000);
                if (cancel) cancellation.CancelAfter(100);
                var elapsed = Stopwatch.StartNew();
                Func<Task> send = async () =>
                {
                    await SingleInstancePipeClient.SendAsync(pipeName, SingleInstanceEndpoint.Current.User,
                        new[] { "request" }, cancellation.Token, TimeSpan.FromMilliseconds(200));
                };
                if (cancel) Assert.CatchAsync<OperationCanceledException>(async () => await send());
                else Assert.ThrowsAsync<TimeoutException>(async () => await send());
                Assert.Less(elapsed.ElapsedMilliseconds, 1500);
                Assert.AreEqual(0, Volatile.Read(ref admitted));
                server.Dispose();
                Assert.AreSame(server.Completion, await Task.WhenAny(server.Completion, Task.Delay(3000)),
                    "The listener must stop while the stalled client is still connected.");
                await server.Completion;
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ForwardedLayoutWaitsUntilTheModalNoticeCloses()
        {
            var user = SingleInstanceEndpoint.Current.User;
            var endpoint = SingleInstanceEndpoint.ForUser(user.Value, Guid.NewGuid().GetHashCode() & Int32.MaxValue);
            var first = new LayoutData("First.xml");
            var second = new LayoutData("Second.xml");
            var original = App.CurrentLayout;
            Task<SingleInstanceReply> reply = null;
            int dispatched = 0;
            int duringNotice = -1;
            var elapsed = Stopwatch.StartNew();

            using (var main = new Form { ShowInTaskbar = false, Opacity = 0 })
            using (var notice = new Form { ShowInTaskbar = false, Opacity = 0 })
            using (var poll = new System.Windows.Forms.Timer { Interval = 20 })
            using (SingleInstanceHelper.StartReceiving(main, endpoint, args =>
            {
                dispatched++;
                App.LoadLayout(second);
            }))
            {
                notice.Shown += (sender, args) => reply = SingleInstancePipeClient.SendAsync(endpoint,
                    new[] { "-layout", second.Name }, CancellationToken.None);
                poll.Tick += (sender, args) =>
                {
                    if (reply?.IsCompleted != true && elapsed.ElapsedMilliseconds < 5000) return;
                    poll.Stop();
                    // The command's callback was posted before the reply. Let it
                    // run in this notice's message loop before recording the state.
                    Application.DoEvents();
                    duringNotice = dispatched;
                    notice.Close();
                };
                EventHandler<LayoutChangedEventArgs> changing = (sender, args) =>
                {
                    if (!ReferenceEquals(args.New, first)) return;
                    args.CompletionNotice = () =>
                    {
                        elapsed.Restart();
                        poll.Start();
                        notice.ShowDialog(main);
                    };
                };
                App.LayoutChanging += changing;
                try
                {
                    main.Show();
                    Application.DoEvents();
                    App.LoadLayout(first);
                    Assert.IsNotNull(reply);
                    Assert.IsTrue(reply.IsCompleted, "The forwarded client did not receive a reply.");
                    Assert.AreEqual(SingleInstanceReply.Accepted, reply.GetAwaiter().GetResult());
                    Assert.AreEqual(0, duringNotice);
                    Assert.AreSame(first, App.CurrentLayout);

                    elapsed.Restart();
                    while (dispatched == 0 && elapsed.ElapsedMilliseconds < 3000)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    Assert.AreEqual(1, dispatched);
                    Assert.AreSame(second, App.CurrentLayout);
                }
                finally
                {
                    App.LayoutChanging -= changing;
                    App.LoadLayout(original, true);
                }
            }
        }

        [Test]
        public void SecondApplicationExecutableForwardsToTheReadyUiAndExits()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPuttyIpcProcess-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string ready = Path.Combine(directory, "ready"), received = Path.Combine(directory, "received"),
                release = Path.Combine(directory, "release");
            Process primary = null, secondary = null;
            try
            {
                string application = typeof(SingleInstanceHelper).Assembly.Location;
                string worker = BuildPrimaryWorker(directory);
                primary = StartProcess(worker, new[] { application, ready, received, release });
                WaitForFile(primary, ready);
                if (File.ReadAllText(ready) == "existing instance")
                    Assert.Ignore("A SuperPuTTY instance is already running in this Windows session.");

                string profile = Path.Combine(directory, "profile");
                Directory.CreateDirectory(profile);
                string putty = Path.Combine(profile, "putty.exe");
                File.WriteAllText(putty, String.Empty);
                File.WriteAllText(Path.Combine(profile, "SuperPuTTY.settings"),
                    "<Settings><PuttyExe>" + SecurityElement.Escape(putty) + "</PuttyExe>" +
                    "<SettingsFolder>" + SecurityElement.Escape(profile) + "</SettingsFolder>" +
                    "<SingleInstanceMode>True</SingleInstanceMode><AutoUpdateCheck>False</AutoUpdateCheck></Settings>");
                string[] arguments = { "-layout", "Forwarded \u03bb layout with \"quotes\"\\" };
                secondary = StartProcess(application, arguments, profile);
                Assert.IsTrue(secondary.WaitForExit(8000), "The second executable did not finish forwarding its request.");
                Assert.AreEqual(0, secondary.ExitCode);
                WaitForFile(primary, received);
                CollectionAssert.AreEqual(arguments, File.ReadAllLines(received)
                    .Select(line => Encoding.UTF8.GetString(Convert.FromBase64String(line))).ToArray());
            }
            finally
            {
                File.WriteAllText(release, "release");
                StopOwnedProcess(secondary);
                StopOwnedProcess(primary);
                Directory.Delete(directory, true);
            }
        }

        private static Process StartProcess(string executable, string[] arguments, string profile = null)
        {
            var start = new ProcessStartInfo(executable, String.Join(" ", arguments.Select(CommandLineOptions.QuoteArgument)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(executable),
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (profile != null) start.EnvironmentVariables["USERPROFILE"] = profile;
            var process = Process.Start(start);
            process.OutputDataReceived += (sender, args) => { };
            process.ErrorDataReceived += (sender, args) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        private static void WaitForFile(Process process, string path)
        {
            var elapsed = Stopwatch.StartNew();
            while (!File.Exists(path) && !process.HasExited && elapsed.ElapsedMilliseconds < 8000) Thread.Sleep(10);
            Assert.IsTrue(File.Exists(path), "The IPC helper did not publish " + Path.GetFileName(path) + ".");
        }

        private static void StopOwnedProcess(Process process)
        {
            if (process == null) return;
            using (process)
                if (!process.WaitForExit(3000)) { process.Kill(); process.WaitForExit(3000); }
        }

        private static string BuildPrimaryWorker(string directory)
        {
            string executable = Path.Combine(directory, "Primary.exe");
            using (var compiler = new CSharpCodeProvider())
            {
                var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = executable };
                options.ReferencedAssemblies.Add("System.dll");
                options.ReferencedAssemblies.Add("System.Windows.Forms.dll");
                options.ReferencedAssemblies.Add("System.Drawing.dll");
                var result = compiler.CompileAssemblyFromSource(options, @"
using System; using System.Collections.Generic; using System.IO; using System.Reflection;
using System.Text; using System.Windows.Forms;
class Primary {
 const BindingFlags InternalStatic=BindingFlags.NonPublic|BindingFlags.Static;
 static void Publish(string path,string value) { File.WriteAllText(path+"".tmp"",value); File.Move(path+"".tmp"",path); }
 [STAThread] static void Main(string[] args) {
  var app=Assembly.LoadFrom(args[0]);
  var mutexType=app.GetType(""SuperPutty.Utils.SingleInstanceMutex"");
  var endpointType=app.GetType(""SuperPutty.Utils.SingleInstanceEndpoint"");
  if((bool)mutexType.GetMethod(""LegacyInstanceExists"",InternalStatic).Invoke(null,null)) { Publish(args[1],""existing instance""); return; }
  var endpoint=endpointType.GetProperty(""Current"",InternalStatic).GetValue(null,null);
  using(var mutex=(IDisposable)Activator.CreateInstance(mutexType,BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{endpoint},null)) {
   if(!(bool)mutexType.GetProperty(""IsPrimary"",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(mutex,null)) { Publish(args[1],""existing instance""); return; }
   using(var form=new Form { ShowInTaskbar=false, Opacity=0, StartPosition=FormStartPosition.Manual, Location=new System.Drawing.Point(-20000,-20000) }) {
    Action<string[]> dispatch=values=> {
     var encoded=new List<string>(); foreach(var value in values) encoded.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
     File.WriteAllLines(args[2]+"".tmp"",encoded); File.Move(args[2]+"".tmp"",args[2]);
    };
    var helper=app.GetType(""SuperPutty.Utils.SingleInstanceHelper"");
    using(var listener=(IDisposable)helper.GetMethod(""StartReceiving"",InternalStatic).Invoke(null,new object[]{form,endpoint,dispatch}))
    using(var timer=new Timer { Interval=20 }) {
     form.Shown+=(s,e)=>form.BeginInvoke(new Action(()=>Publish(args[1],""ready"")));
     var deadline=DateTime.UtcNow.AddSeconds(25);
     timer.Tick+=(s,e)=> { if(File.Exists(args[3]) || DateTime.UtcNow>deadline) form.Close(); };
     timer.Start(); Application.Run(form);
    }
   }
  }
 }
}");
                Assert.IsFalse(result.Errors.HasErrors, String.Join("\n", result.Errors.Cast<CompilerError>()));
            }
            return executable;
        }
    }
}
