using System;
using System.Diagnostics;
using System.CodeDom.Compiler;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.CSharp;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class VncStartupTests
    {
        [Test]
        [Apartment(ApartmentState.STA)]
        public void FindsDesktopAfterLoginAndAgainAfterReconnect()
        {
            uint processId = (uint)Process.GetCurrentProcess().Id;
            using (Form login = CreateViewerWindow("VNC authentication"))
            using (Form desktop = CreateViewerWindow("Test desktop - TigerVNC"))
            using (Form options = CreateViewerWindow("TigerVNC options"))
            using (Form reconnected = CreateViewerWindow("Reconnected desktop - TigerVNC (keyboard grabbed)"))
            {
                login.Show();
                Assert.AreEqual(IntPtr.Zero, ApplicationPanel.FindTigerVncDesktopWindow(processId));
                Assert.IsFalse(ApplicationPanel.IsTigerVncDesktopWindow(login.Handle));

                // The login can remain alive while the separate desktop appears.
                desktop.Show();
                options.Show();
                Assert.AreEqual(desktop.Handle, ApplicationPanel.FindTigerVncDesktopWindow(processId));
                Assert.AreEqual(IntPtr.Zero, ApplicationPanel.FindTigerVncDesktopWindow(uint.MaxValue));
                desktop.Hide();
                Assert.IsTrue(ApplicationPanel.IsTigerVncDesktopWindow(desktop.Handle),
                    "A captured desktop in a hidden tab must not be replaced by a dialog.");

                IntPtr oldHandle = desktop.Handle;
                desktop.Dispose();
                Assert.IsFalse(ApplicationPanel.IsTigerVncDesktopWindow(oldHandle));
                Assert.AreEqual(IntPtr.Zero, ApplicationPanel.FindTigerVncDesktopWindow(processId));
                reconnected.Show();
                Assert.AreEqual(reconnected.Handle, ApplicationPanel.FindTigerVncDesktopWindow(processId));
            }
        }

        private static Form CreateViewerWindow(string title)
        {
            return new Form
            {
                Text = title, ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-20000, -20000)
            };
        }

        [Test]
        public void RenamedViewerIsRecognizedByProductMetadata()
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string executable = Path.Combine(directory, "vncviewer.exe");
            try
            {
                using (CSharpCodeProvider compiler = new CSharpCodeProvider())
                {
                    CompilerResults result = compiler.CompileAssemblyFromSource(
                        new CompilerParameters { GenerateExecutable = true, OutputAssembly = executable },
                        "[assembly: System.Reflection.AssemblyProduct(\"TigerVNC\")] class Viewer { static void Main() {} }");
                    Assert.IsFalse(result.Errors.HasErrors);
                }
                VNCStartInfo info = new VNCStartInfo(new SessionData { Host = "server", Port = 5902 }, executable);
                Assert.AreEqual("server::5902", info.Args);
            }
            finally
            {
                File.Delete(executable);
                Directory.Delete(directory);
            }
        }

        [TestCase("192.168.5.240", 5902, "192.168.5.240::5902")]
        [TestCase("server", 0, "server")]
        [TestCase("2001:db8::1", 5902, "[2001:db8::1]::5902")]
        [TestCase("[2001:db8::1]", 5900, "[2001:db8::1]::5900")]
        public void TigerVncUsesExplicitPortEndpoint(string host, int port, string expected)
        {
            VNCStartInfo info = new VNCStartInfo(new SessionData { Host = host, Port = port },
                @"C:\TigerVNC\vncviewer.exe");
            Assert.AreEqual(expected, info.Args);
            Assert.AreEqual(expected, info.ArgsForLog);
        }

        [Test]
        public void TigerVncKeepsOptionsWithoutUnsupportedPasswordArgument()
        {
            VNCStartInfo info = new VNCStartInfo(new SessionData
            {
                Host = "server", Port = 5902, Password = "saved-secret",
                ExtraArgs = "-ViewOnly -password=extra-secret"
            }, @"C:\Downloads\tigervnc64-1.15.0.exe");
            StringAssert.Contains("-ViewOnly", info.Args);
            StringAssert.EndsWith("server::5902", info.Args);
            StringAssert.DoesNotContain("secret", info.Args);
            StringAssert.DoesNotContain("password", info.Args);
            Assert.AreEqual(info.Args, info.ArgsForLog);
        }

        [Test]
        public void TightVncRetainsExistingArguments()
        {
            VNCStartInfo info = new VNCStartInfo(new SessionData { Host = "server", Port = 5902 },
                @"C:\TightVNC\tvnviewer.exe");
            Assert.AreEqual("-scale=auto -port=5902 server", info.Args);
        }

        [Test]
        public void ExitedProcessDoesNotThrowWhileLookingForWindow()
        {
            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC"),
                Arguments = "/d /c exit 7", UseShellExecute = false, CreateNoWindow = true
            }))
            {
                Assert.IsTrue(process.WaitForExit(5000));
                IntPtr window;
                Assert.IsFalse(ApplicationPanel.TryGetProcessWindow(process, out window));
                Assert.AreEqual(IntPtr.Zero, window);
                Assert.AreEqual(7, process.ExitCode);
            }
        }
    }
}
