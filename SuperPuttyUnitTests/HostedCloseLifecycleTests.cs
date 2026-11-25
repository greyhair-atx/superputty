using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.CSharp;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Utils;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class HostedCloseLifecycleTests
    {
        private string directory;
        private string executable;

        private sealed class FailingRecreationPanel : ApplicationPanel
        {
            internal bool RejectDetach;
            internal bool RejectRestore;
            internal bool InterruptCreation;
            internal FailingRecreationPanel() : base(ConnectionProtocol.SSH) { }
            protected override IntPtr RecreationWindow { get { return RejectDetach ? new IntPtr(-1) : base.RecreationWindow; } }
            protected override bool OwnsRecreationWindow(IntPtr window) { return !RejectRestore && base.OwnsRecreationWindow(window); }
            protected override void OnHandleCreated(EventArgs e)
            {
                if (InterruptCreation) return;
                base.OnHandleCreated(e);
            }
        }

        [OneTimeSetUp]
        public void CompileHostedWindowFixture()
        {
            directory = Path.Combine(Path.GetTempPath(), "SuperPuttyHostedClose-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            executable = Path.Combine(directory, "HostedWindow.exe");
            using (var compiler = new CSharpCodeProvider())
            {
                var options = new CompilerParameters
                {
                    GenerateExecutable = true,
                    OutputAssembly = executable,
                    CompilerOptions = "/target:winexe"
                };
                options.ReferencedAssemblies.Add("System.dll");
                options.ReferencedAssemblies.Add("System.Drawing.dll");
                options.ReferencedAssemblies.Add("System.Windows.Forms.dll");
                var compiled = compiler.CompileAssemblyFromSource(options, @"
using System; using System.IO; using System.Threading; using System.Windows.Forms;
class HostedWindow : Form {
 readonly string marker; readonly int closeMessage, delay; bool armed;
 HostedWindow(string path, int message, int milliseconds) {
  marker=path; closeMessage=message; delay=milliseconds;
  StartPosition=FormStartPosition.Manual;
  Location=new System.Drawing.Point(-20000,-20000);
  Shown += delegate {
   armed=true;
   File.WriteAllText(marker+"".tmp"", Handle.ToInt64().ToString());
   File.Move(marker+"".tmp"", marker);
  };
 }
 protected override bool ShowWithoutActivation { get { return true; } }
 protected override void WndProc(ref Message message) {
  if(message.Msg==0x8001) Text=""Fixture - Remote Desktop Connection"";
  if(message.Msg==0x8002) Hide();
  if(armed && message.Msg==closeMessage) {
   File.WriteAllText(marker+"".closed"", message.Msg.ToString());
   Thread.Sleep(delay); Environment.Exit(0);
  }
  base.WndProc(ref message);
 }
 [STAThread] static void Main(string[] args) {
  if(args[0]==""exit"") return;
  Application.Run(new HostedWindow(args[0], Int32.Parse(args[1]), Int32.Parse(args[2])));
 }
}");
                Assert.IsFalse(compiled.Errors.HasErrors,
                    String.Join("\n", compiled.Errors.Cast<CompilerError>().Select(error => error.ToString())));
            }
        }

        [OneTimeTearDown]
        public void RemoveHostedWindowFixture()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExitingClientClosesItsTabAndUnresponsiveClientIsTerminated(bool ignoresClose)
        {
            string marker = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".window");
            var session = new SessionData { Proto = ConnectionProtocol.SSH, SessionName = "Hosted lifecycle test" };
            using (var tab = new ctlPuttyPanel(session, null))
            {
                var client = tab.AppPanel;
                client.ApplicationName = executable;
                client.ApplicationParameters = CommandLineOptions.QuoteArgument(marker) + " " +
                    NativeMethods.WM_CLOSE + " " + (ignoresClose ? 60000 : 0);
                client.ApplicationCloseWithDestroy = false;
                tab.Show();
                tab.WatchStartup();
                PumpUntil(() => tab.Startup.IsCompleted);
                Assert.IsNull(tab.Startup.Result);
                Assert.IsTrue(client.ExternalProcessCaptured);

                uint pid;
                NativeMethods.GetWindowThreadProcessId(client.AppWindowHandle, out pid);
                using (var process = Process.GetProcessById((int)pid))
                {
                    if (ignoresClose)
                        tab.CloseWithoutConfirmation();
                    else
                        Assert.IsTrue(NativeMethods.PostMessage(client.AppWindowHandle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
                    PumpUntil(() => tab.IsDisposed);
                    Assert.IsTrue(process.WaitForExit(5000), "The owned child was left running.");
                    PumpUntil(() => client.ProcessCleanup.IsCompleted);
                }
            }
        }

        [Test]
        public void DockingRoundTripAndHostHandleRecreationPreserveTheLiveClient()
        {
            string marker = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".window");
            using (var form = new Form { Width = 1000, Height = 700 })
            using (var dock = new DockPanel { Dock = DockStyle.Fill, DocumentStyle = DocumentStyle.DockingWindow })
            using (var theme = new VS2015LightTheme())
            using (var tab = new ctlPuttyPanel(new SessionData { Proto = ConnectionProtocol.SSH, SessionName = "Dock round trip" }, null))
            {
                dock.Theme = theme;
                form.Controls.Add(dock);
                var client = tab.AppPanel;
                client.ApplicationName = executable;
                client.ApplicationParameters = CommandLineOptions.QuoteArgument(marker) + " " + NativeMethods.WM_CLOSE + " 0";
                client.ApplicationCloseWithDestroy = false;
                form.Show();
                tab.Show(dock, DockState.Document);
                tab.WatchStartup();
                PumpUntil(() => tab.Startup.IsCompleted);
                Assert.IsNull(tab.Startup.Result);
                IntPtr child = client.AppWindowHandle;
                uint pid;
                NativeMethods.GetWindowThreadProcessId(child, out pid);
                using (var process = Process.GetProcessById((int)pid))
                {
                    try
                    {
                        foreach (DockState state in new[] { DockState.DockLeft, DockState.Float, DockState.Document })
                        {
                            tab.Show(dock, state);
                            Application.DoEvents();
                            Assert.AreEqual(state, tab.DockState);
                            Assert.AreSame(state == DockState.Float ? (Form)tab.Pane.FloatWindow : form, client.ActivationForm);
                            AssertHostedClient(client, child, process);

                            // Exercise the exceptional lifecycle as well as normal reparenting.
                            typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)
                                .Invoke(client, null);
                            Application.DoEvents();
                            AssertHostedClient(client, child, process);
                            Assert.IsFalse(client.ScriptsStopped);

                            typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)
                                .Invoke(tab, null);
                            Application.DoEvents();
                            AssertHostedClient(client, child, process);
                        }
                    }
                    finally
                    {
                        tab.CloseWithoutConfirmation();
                        PumpUntil(() => client.ProcessCleanup.IsCompleted);
                        if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
                    }
                    Assert.IsTrue(process.HasExited);
                    Assert.IsTrue(File.Exists(marker + ".closed"), "Final close must still notify the original client.");
                }
            }
        }

        private static void AssertHostedClient(ApplicationPanel client, IntPtr child, Process process)
        {
            Assert.IsFalse(process.HasExited, "Docking must preserve the original process.");
            Assert.AreEqual(child, client.AppWindowHandle, "Docking must preserve the native client window.");
            Assert.AreEqual(client.Handle, NativeMethods.GetAncestor(child, 1));
            NativeMethods.RECT bounds = new NativeMethods.RECT();
            Assert.IsTrue(NativeMethods.GetWindowRect(child, ref bounds));
            var expected = client.RectangleToScreen(client.HostedWindowBounds);
            Assert.AreEqual(expected, System.Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void FailedHandleRecreationReleasesParkingHostAndOwnedProcess(bool detachFails, bool interruptCreation)
        {
            string marker = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".window");
            using (var form = new Form())
            using (var client = new FailingRecreationPanel
            {
                ApplicationName = executable,
                ApplicationParameters = CommandLineOptions.QuoteArgument(marker) + " " + NativeMethods.WM_CLOSE + " 0",
                ApplicationCloseWithDestroy = false,
                Dock = DockStyle.Fill
            })
            {
                string failure = null;
                client.StartupFailed = message => failure = message;
                form.Controls.Add(client);
                form.Show();
                PumpUntil(() => client.ExternalProcessCaptured);
                uint pid;
                NativeMethods.GetWindowThreadProcessId(client.AppWindowHandle, out pid);
                using (var process = Process.GetProcessById((int)pid))
                {
                    try
                    {
                        client.RejectDetach = detachFails;
                        client.RejectRestore = !detachFails;
                        client.InterruptCreation = interruptCreation;
                        var recreate = typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic);
                        var parkingField = typeof(ApplicationPanel).GetField("recreationHost", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (interruptCreation)
                        {
                            Assert.DoesNotThrow(() => recreate.Invoke(client, null));
                            var parking = (Panel)parkingField.GetValue(client);
                            Assert.IsNotNull(parking);
                            client.Dispose();
                            Assert.IsTrue(parking.IsDisposed);
                        }
                        else Assert.DoesNotThrow(() => recreate.Invoke(client, null));
                        PumpUntil(() => client.ProcessCleanup.IsCompleted);
                        if (!interruptCreation) Assert.IsNotNull(failure);
                        Assert.IsFalse(client.ExternalProcessCaptured);
                        Assert.IsTrue(process.HasExited);
                        Assert.IsNull(parkingField.GetValue(client));
                    }
                    finally
                    {
                        if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
                    }
                }
            }
        }

        [Test]
        public void ProcessExitBeforeWindowCaptureLeavesARetryableTab()
        {
            using (var tab = new ctlPuttyPanel(new SessionData { Proto = ConnectionProtocol.SSH, SessionName = "Early exit" }, null))
            {
                tab.AppPanel.ApplicationName = executable;
                tab.AppPanel.ApplicationParameters = "exit";
                tab.Show();
                tab.WatchStartup();
                PumpUntil(() => tab.Startup.IsCompleted);
                Assert.IsNotNull(tab.Startup.Result);
                Assert.IsTrue(tab.AppPanel.IsDisposed);
                Assert.IsFalse(tab.IsDisposed);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ExternalRdpWatchdogWaitsForSignInAndResumesWhenPromptCloses(bool completesSignIn)
        {
            var settings = SuperPutty.SuperPuTTY.Settings;
            string original = settings.RDPExe;
            string viewerDirectory = Path.Combine(directory, "rdp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(viewerDirectory);
            string viewer = Path.Combine(viewerDirectory, "wfreerdp.exe");
            File.Copy(executable, viewer);
            settings.RDPExe = viewer;
            try
            {
                var session = new SessionData { Proto = ConnectionProtocol.RDP, SessionName = "RDP sign-in" };
                using (var tab = new ctlPuttyPanel(session, null))
                {
                    string marker = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".window");
                    var client = tab.AppPanel;
                    client.ApplicationParameters = CommandLineOptions.QuoteArgument(marker) + " " +
                        NativeMethods.WM_DESTROY + " 0";
                    tab.Show();
                    tab.WatchStartup();
                    PumpUntil(() => client.IsWaitingForStartupInput && File.Exists(marker));

                    var watchdog = (System.Windows.Forms.Timer)typeof(ctlPuttyPanel)
                        .GetField("startupTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tab);
                    watchdog.Interval = 100;
                    tab.WatchStartup();
                    Assert.IsFalse(watchdog.Enabled);
                    Assert.IsFalse(tab.Startup.IsCompleted);
                    Assert.IsFalse(client.ExternalProcessCaptured);

                    IntPtr prompt = new IntPtr(Int64.Parse(File.ReadAllText(marker)));
                    Assert.IsTrue(NativeMethods.PostMessage(prompt, completesSignIn ? 0x8001u : 0x8002u, IntPtr.Zero, IntPtr.Zero));
                    PumpUntil(() => tab.Startup.IsCompleted);
                    if (completesSignIn)
                    {
                        Assert.IsNull(tab.Startup.Result);
                        Assert.IsTrue(client.ExternalProcessCaptured);
                    }
                    else
                    {
                        StringAssert.Contains("timed out", tab.Startup.Result);
                        Assert.IsTrue(client.IsDisposed);
                    }
                    tab.CloseWithoutConfirmation();
                    PumpUntil(() => client.ProcessCleanup.IsCompleted);
                }
            }
            finally { settings.RDPExe = original; }
        }

        private static void PumpUntil(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!condition() && timer.ElapsedMilliseconds < 5000)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Assert.IsTrue(condition(), "The hosted client did not reach the expected state.");
        }

        [TestCase(false, false, 0)]
        [TestCase(false, true, 0)]
        [TestCase(true, true, 0)]
        [TestCase(false, true, 250)]
        public void DisposingHostedPanelLetsTheChildProcessExit(bool closeWithDestroy, bool preserveWindowStyle, int closeDelay)
        {
            string marker = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".window");
            int expectedMessage = closeWithDestroy ? NativeMethods.WM_DESTROY : NativeMethods.WM_CLOSE;
            string arguments = CommandLineOptions.QuoteArgument(marker) + " " + expectedMessage + " " + closeDelay;
            using (var process = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }))
            {
                try
                {
                    var ready = Stopwatch.StartNew();
                    while (!File.Exists(marker) && !process.HasExited && ready.ElapsedMilliseconds < 5000)
                        Thread.Sleep(10);
                    Assert.IsTrue(File.Exists(marker), "The local hosted-window fixture did not become ready.");
                    var child = new IntPtr(Int64.Parse(File.ReadAllText(marker)));
                    Assert.IsTrue(NativeMethods.IsWindow(child));
                    using (var panel = new ApplicationPanel(ConnectionProtocol.SSH)
                    {
                        ApplicationCloseWithDestroy = closeWithDestroy
                    })
                    {
                        ApplicationPanel.ReparentWindow(child, panel.Handle, preserveWindowStyle);
                        typeof(ApplicationPanel).GetField("m_AppWin", BindingFlags.Instance | BindingFlags.NonPublic)
                            .SetValue(panel, child);
                        typeof(ApplicationPanel).GetField("m_Process", BindingFlags.Instance | BindingFlags.NonPublic)
                            .SetValue(panel, process);
                        Assert.IsTrue(panel.ExternalProcessCaptured);
                    }

                    Assert.IsTrue(process.WaitForExit(5000), "Disposing the host left its client process running.");
                    Assert.AreEqual(0, process.ExitCode);
                    Assert.IsTrue(File.Exists(marker + ".closed"), "The client did not receive its configured close message.");
                    Assert.AreEqual(expectedMessage.ToString(), File.ReadAllText(marker + ".closed"));
                }
                finally
                {
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
                }
            }
        }
    }
}
