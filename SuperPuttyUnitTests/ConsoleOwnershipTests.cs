using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Scp;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class ConsoleOwnershipTests
    {
        private sealed class ConsoleWindow : NativeWindow, IDisposable
        {
            internal int CloseRequests;

            internal ConsoleWindow()
            {
                CreateHandle(new CreateParams { Caption = "Console ownership fixture", Width = 320, Height = 240,
                    Style = unchecked((int)(NativeMethods.WS_POPUP | NativeMethods.WS_CAPTION)) });
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0010) { CloseRequests++; return; }
                base.WndProc(ref message);
            }

            public void Dispose() { DestroyHandle(); }
        }

        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void WindowWithDifferentProcessOrParentIsNeverMovedFocusedOrClosed(bool sameProcess, bool sameParent)
        {
            // Keep a distinct process alive without creating a console or a child tree.
            using (var foreign = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"), Arguments = "/d /q /k",
                UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true
            }))
            {
                try
                {
                    using (var panel = new ConsoleApplicationPanel(ConnectionProtocol.WINCMD))
                    using (var otherHost = new Panel())
                    using (var window = new ConsoleWindow())
                    {
                        panel.Size = new Size(640, 480);
                        var createdHandle = panel.Handle;
                        ApplicationPanel.ReparentWindow(window.Handle, sameParent ? panel.Handle : otherHost.Handle, true);
                        SetCapturedWindow(panel, window.Handle,
                            Process.GetProcessById(sameProcess ? Process.GetCurrentProcess().Id : foreign.Id));
                        int style = NativeMethods.GetWindowLong(window.Handle, NativeMethods.GWL_STYLE);
                        var before = new NativeMethods.RECT();
                        Assert.IsTrue(NativeMethods.GetWindowRect(window.Handle, ref before));

                        Assert.IsFalse(panel.ExternalProcessCaptured);
                        Assert.AreEqual(IntPtr.Zero, panel.AppWindowHandle);
                        if (!sameProcess)
                            Assert.IsFalse((bool)typeof(ConsoleApplicationPanel).GetMethod("AttachConsoleWindow",
                                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null));
                        panel.RefreshAppWindow();
                        panel.Size = new Size(800, 600);
                        Assert.IsFalse(panel.ReFocusPuTTY("Ownership regression"));
                        CloseConsole(panel);
                        Application.DoEvents();

                        var after = new NativeMethods.RECT();
                        Assert.IsTrue(IsWindow(window.Handle));
                        Assert.IsTrue(NativeMethods.GetWindowRect(window.Handle, ref after));
                        Assert.AreEqual(before.ToString(), after.ToString());
                        Assert.AreEqual(style, NativeMethods.GetWindowLong(window.Handle, NativeMethods.GWL_STYLE));
                        Assert.AreEqual(0, window.CloseRequests);
                    }
                }
                finally
                {
                    if (!foreign.HasExited)
                    {
                        foreign.StandardInput.WriteLine("exit");
                        foreign.StandardInput.Flush();
                        if (!foreign.WaitForExit(5000)) foreign.Kill();
                    }
                }
            }
        }

        [Test]
        public void ScpConsoleRejectsWindowsWithDifferentOwnersThreadsOrParents()
        {
            using (var expectedHost = new Panel())
            using (var otherHost = new Panel())
            using (var window = new ConsoleWindow())
            {
                ApplicationPanel.ReparentWindow(window.Handle, expectedHost.Handle, true);
                uint owner;
                uint thread = NativeMethods.GetWindowThreadProcessId(window.Handle, out owner);
                Assert.True(InteractivePscpDialog.IsOwnedConsoleWindow(window.Handle, owner, thread, expectedHost.Handle));
                Assert.False(InteractivePscpDialog.IsOwnedConsoleWindow(window.Handle, owner + 1, thread, expectedHost.Handle));
                Assert.False(InteractivePscpDialog.IsOwnedConsoleWindow(window.Handle, owner, thread + 1, expectedHost.Handle));
                ApplicationPanel.ReparentWindow(window.Handle, otherHost.Handle, true);
                Assert.False(InteractivePscpDialog.IsOwnedConsoleWindow(window.Handle, owner, thread, expectedHost.Handle));
            }
        }

        [Test]
        public void OwnedConsoleCanBeResizedAndClosed()
        {
            using (var panel = new ConsoleApplicationPanel(ConnectionProtocol.PS))
            using (var window = new ConsoleWindow())
            {
                panel.Size = new Size(640, 480);
                ApplicationPanel.ReparentWindow(window.Handle, panel.Handle, true);
                SetCapturedWindow(panel, window.Handle, Process.GetCurrentProcess());
                Assert.IsTrue(panel.ExternalProcessCaptured);
                Assert.AreEqual(window.Handle, panel.AppWindowHandle);
                panel.RefreshAppWindow();
                var bounds = new NativeMethods.RECT();
                Assert.IsTrue(NativeMethods.GetWindowRect(window.Handle, ref bounds));
                Assert.AreEqual(panel.Width, bounds.Right - bounds.Left);
                Assert.AreEqual(panel.Height, bounds.Bottom - bounds.Top);
                CloseConsole(panel);
                Application.DoEvents();
                Assert.AreEqual(1, window.CloseRequests);
            }
        }

        [Test]
        public void UnavailableCapturedProcessIsHandledAsUncaptured()
        {
            using (var panel = new ConsoleApplicationPanel(ConnectionProtocol.WINCMD))
            using (var window = new ConsoleWindow())
            {
                ApplicationPanel.ReparentWindow(window.Handle, panel.Handle, true);
                SetCapturedWindow(panel, window.Handle, new Process());
                Assert.IsFalse(panel.ExternalProcessCaptured);
                Assert.AreEqual(IntPtr.Zero, panel.AppWindowHandle);
            }
        }

        [Test]
        public void ProcessAncestryRejectsMissingOrCyclicParents()
        {
            var parents = new Dictionary<uint, uint> { { 20, 10 }, { 30, 20 }, { 40, 50 }, { 50, 40 } };
            Assert.IsTrue(ConsoleApplicationPanel.IsDescendantProcess(30, 10, parents));
            Assert.IsFalse(ConsoleApplicationPanel.IsDescendantProcess(30, 40, parents));
            Assert.IsFalse(ConsoleApplicationPanel.IsDescendantProcess(40, 10, parents));
            Assert.IsFalse(ConsoleApplicationPanel.IsDescendantProcess(99, 10, parents));
        }

        private static void SetCapturedWindow(ConsoleApplicationPanel panel, IntPtr window, Process process)
        {
            typeof(ConsoleApplicationPanel).GetField("consoleWindow", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(panel, window);
            typeof(ConsoleApplicationPanel).GetField("consoleProcess", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(panel, process);
        }

        private static void CloseConsole(ConsoleApplicationPanel panel)
        {
            typeof(ConsoleApplicationPanel).GetMethod("CloseConsole", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
        }
    }
}
