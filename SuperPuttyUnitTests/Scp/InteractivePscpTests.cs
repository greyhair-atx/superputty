using System;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.CSharp;
using NUnit.Framework;
using SuperPutty.Data;
using SuperPutty.Scp;

namespace SuperPuttyUnitTests.Scp
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class InteractivePscpTests
    {
        [Test]
        public void InteractiveModePersistsAndClonesWithoutChangingTheDefault()
        {
            Assert.IsFalse(new SessionData().ScpInteractiveAuthentication);
            var source = new SessionData { ScpInteractiveAuthentication = true };
            Assert.IsTrue(((SessionData)source.Clone()).ScpInteractiveAuthentication);
            var serializer = new XmlSerializer(typeof(SessionData));
            using (var text = new StringWriter())
            {
                serializer.Serialize(text, source);
                using (var input = new StringReader(text.ToString()))
                    Assert.IsTrue(((SessionData)serializer.Deserialize(input)).ScpInteractiveAuthentication);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InteractiveCommandsNeverSupplyCredentialsOrBatchMode(bool plaintextEnabled)
        {
            var settings = SuperPutty.SuperPuTTY.Settings;
            bool original = settings.AllowPlainTextPuttyPasswordArg;
            try
            {
                settings.AllowPlainTextPuttyPasswordArg = plaintextEnabled;
                var session = new SessionData
                {
                    ScpInteractiveAuthentication = true, Username = "test", Host = "example.invalid", Port = 22,
                    ExtraArgs = "-batch -pw extra-secret -pwfile \"C:\\secret file\" -v -hostkey fingerprint"
                };
                var target = new BrowserFileInfo { Source = SourceType.Remote, Path = "/home/test" };
                string[] commands = {
                    PscpClient.ToArgs(session, "stored-secret", target.Path, "password-pipe"),
                    PscpClient.ToArgs(session, "stored-secret", new List<BrowserFileInfo> {
                        new BrowserFileInfo { Source = SourceType.Local, Path = "C:\\sample file" } }, target, "password-pipe")
                };
                foreach (string command in commands)
                {
                    StringAssert.DoesNotContain("-batch", command);
                    StringAssert.DoesNotContain("-pw", command);
                    StringAssert.DoesNotContain("secret", command);
                    StringAssert.DoesNotContain("password-pipe", command);
                    StringAssert.Contains("-hostkey fingerprint", command);
                    StringAssert.Contains("-v", command);
                }
            }
            finally { settings.AllowPlainTextPuttyPasswordArg = original; }
        }

        private string directory, client;

        [OneTimeSetUp]
        public void BuildConsoleClient()
        {
            directory = Path.Combine(Path.GetTempPath(), "SuperPutty-Mfa-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            client = Path.Combine(directory, "ConsoleClient.exe");
            using (var compiler = new CSharpCodeProvider())
            {
                var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = client };
                options.ReferencedAssemblies.Add("System.dll");
                var compiled = compiler.CompileAssemblyFromSource(options, ClientSource);
                Assert.IsFalse(compiled.Errors.HasErrors, String.Join("\n", compiled.Errors.Cast<CompilerError>()));
            }
        }

        [OneTimeTearDown]
        public void RemoveConsoleClient()
        {
            File.Delete(client);
            Directory.Delete(directory);
        }

        [Test]
        [Category("InteractiveConsoleTest")]
        public void TwoConsoleChallengesKeepOneProcessAndOnlyForwardListingOutput()
        {
            bool embedded = false;
            using (var dialog = new InteractivePscpDialog(client, "mfa", 10000, CancellationToken.None, null))
            using (var watchdog = new System.Windows.Forms.Timer { Interval = 50 })
            {
                watchdog.Tick += delegate
                {
                    var window = (IntPtr)typeof(InteractivePscpDialog).GetField("consoleWindow", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                    if (window != IntPtr.Zero) embedded |= GetParent(window) != IntPtr.Zero;
                    if (dialog.Result.Error != null) dialog.Close();
                };
                watchdog.Start();
                dialog.ShowDialog();
                Assert.AreEqual(0, dialog.Result.ExitCode, dialog.Result.Error);
                Assert.IsFalse(dialog.Result.Cancelled);
                CollectionAssert.AreEqual(new[] { "listing-after-two-challenges" }, dialog.Result.Output);
                Assert.IsTrue(embedded, "Console was not embedded.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("InteractiveConsoleTest")]
        public void CancelAndTimeoutTerminateTheConsoleProcessTree(bool timeout)
        {
            int childId = 0;
            using (var cancellation = new CancellationTokenSource())
            // Allow the mock runtime and its child process to start on a busy
            // agent before asserting that timeout kills the complete tree.
            using (var dialog = new InteractivePscpDialog(client, "hang", timeout ? 5000 : 10000, cancellation.Token,
                line => { Int32.TryParse(line, out childId); return true; }))
            using (var watchdog = new System.Windows.Forms.Timer { Interval = 100 })
            {
                var elapsed = Stopwatch.StartNew();
                watchdog.Tick += delegate
                {
                    if (!timeout && childId != 0) cancellation.Cancel();
                    if (dialog.Result.Error != null || elapsed.ElapsedMilliseconds > 12000) dialog.Close();
                };
                watchdog.Start();
                dialog.ShowDialog();
                Assert.AreNotEqual(0, childId, "The console client did not start.");
                Assert.AreNotEqual(0, dialog.Result.ExitCode);
                Assert.AreEqual(!timeout, dialog.Result.Cancelled);
                if (timeout) StringAssert.Contains("timed out", dialog.Result.Error);
            }
            try
            {
                using (var process = Process.GetProcessById(childId))
                    Assert.IsTrue(process.WaitForExit(5000), "Console client survived cancellation.");
            }
            catch (ArgumentException) { } // Already reaped.
        }

        [Test]
        [Category("InteractiveConsoleTest")]
        public void FailedProcessKeepsItsExitCodeInsteadOfRetryingAuthentication()
        {
            using (var dialog = new InteractivePscpDialog(client, "fail", 10000, CancellationToken.None, null))
            using (var watchdog = new System.Windows.Forms.Timer { Interval = 100 })
            {
                watchdog.Tick += delegate { if (!dialog.Result.Cancelled) dialog.Close(); };
                watchdog.Start();
                dialog.ShowDialog();
                Assert.AreEqual(7, dialog.Result.ExitCode);
                Assert.IsFalse(dialog.Result.Cancelled);
                Assert.IsEmpty(dialog.Result.Output);
            }
        }

        [TestCase("long-output")]
        [TestCase("many-lines")]
        [Category("InteractiveConsoleTest")]
        public void OversizedConsoleOutputFailsAndTerminatesItsProcessTree(string mode)
        {
            int childId = 0;
            using (var dialog = new InteractivePscpDialog(client, mode, 15000, CancellationToken.None,
                line => { if (childId == 0) Int32.TryParse(line, out childId); return true; }))
            using (var watchdog = new System.Windows.Forms.Timer { Interval = 50 })
            {
                var elapsed = Stopwatch.StartNew();
                watchdog.Tick += delegate
                {
                    if (dialog.Result.Error != null || elapsed.ElapsedMilliseconds > 20000) dialog.Close();
                };
                watchdog.Start();
                dialog.ShowDialog();
                Assert.AreNotEqual(0, childId, "The output fixture did not start.");
                Assert.AreEqual(ScpOutputLimitException.SafeMessage, dialog.Result.Error);
                Assert.IsFalse(dialog.Result.Cancelled);
                Assert.IsEmpty(dialog.Result.Output, "Do not publish a partial listing.");
            }
            try
            {
                using (var process = Process.GetProcessById(childId))
                    Assert.IsTrue(process.WaitForExit(5000), "Console client survived its output-limit failure.");
            }
            catch (ArgumentException) { }
        }

        [Test]
        [Category("InteractiveConsoleTest")]
        public void ClosingTheOwnerCancelsItsAuthenticationConsole()
        {
            int childId = 0;
            using (var owner = new Form { ShowInTaskbar = false })
            using (var dialog = new InteractivePscpDialog(client, "hang", 10000, CancellationToken.None,
                line => { Int32.TryParse(line, out childId); return true; }))
            {
                owner.Show();
                dialog.Show(owner);
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (childId == 0 && DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(20);
                }
                Assert.AreNotEqual(0, childId, "The console client did not start.");
                owner.Close();
                Application.DoEvents();
                Assert.IsTrue(dialog.Result.Cancelled);
            }
            try
            {
                using (var process = Process.GetProcessById(childId))
                    Assert.IsTrue(process.WaitForExit(5000), "Console client survived owner shutdown.");
            }
            catch (ArgumentException) { }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);

        private const string ClientSource = @"
using System; using System.IO; using System.Runtime.InteropServices; using System.Threading;
class Client {
 [StructLayout(LayoutKind.Explicit, Size=20)] struct Input {
  [FieldOffset(0)] public short type; [FieldOffset(4)] public int down;
  [FieldOffset(8)] public short repeat; [FieldOffset(10)] public short key;
  [FieldOffset(14)] public char character;
 }
 [DllImport(""kernel32.dll"",CharSet=CharSet.Unicode)] static extern IntPtr CreateFile(string n,uint a,uint s,IntPtr sa,uint d,uint f,IntPtr t);
 [DllImport(""kernel32.dll"")] static extern bool GetConsoleMode(IntPtr h,out uint mode);
 [DllImport(""kernel32.dll"")] static extern bool SetConsoleMode(IntPtr h,uint mode);
 [DllImport(""kernel32.dll"",CharSet=CharSet.Unicode)] static extern bool WriteConsoleInput(IntPtr h,Input[] input,uint count,out uint written);
 [DllImport(""kernel32.dll"",CharSet=CharSet.Unicode)] static extern bool ReadConsole(IntPtr h,char[] data,uint count,out uint read,IntPtr control);
 static int Main(string[] args) {
  if(args[0]==""fail"") { Console.Error.WriteLine(""Authentication rejected""); return 7; }
  if(args[0]==""long-output"" || args[0]==""many-lines"") {
   Console.WriteLine(System.Diagnostics.Process.GetCurrentProcess().Id);
   if(args[0]==""long-output"") Console.Write(new string('x',65540));
   else Console.Write(new string('\n',100005));
   Console.Out.Flush(); Thread.Sleep(Timeout.Infinite);
  }
  var input=CreateFile(""CONIN$"",0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
  uint original; if(!GetConsoleMode(input,out original)) return 10;
  if(args[0]==""hang"") { Console.WriteLine(System.Diagnostics.Process.GetCurrentProcess().Id); Console.Error.Write(""Verification code: ""); Thread.Sleep(Timeout.Infinite); }
  foreach(string response in new[]{""test-password"",""123456""}) {
   Console.Error.Write(response==""123456"" ? ""Verification code: "" : ""Password: "");
   SetConsoleMode(input,(original | 3u) & ~4u);
   string text=response+""\r""; var events=new Input[text.Length];
   for(int i=0;i<text.Length;i++) events[i]=new Input{type=1,down=1,repeat=1,character=text[i],key=(short)(text[i]=='\r'?13:0)};
   uint written; if(!WriteConsoleInput(input,events,(uint)events.Length,out written)) return 11;
   var data=new char[128]; uint read; if(!ReadConsole(input,data,128,out read,IntPtr.Zero)) return 12;
   if(new string(data,0,(int)read).TrimEnd('\r','\n')!=response) return 13;
   SetConsoleMode(input,original);
  }
  Thread.Sleep(250); Console.WriteLine(""listing-after-two-challenges""); return 0;
 }
}";
    }
}
