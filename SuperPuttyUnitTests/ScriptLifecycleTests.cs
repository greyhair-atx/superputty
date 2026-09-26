using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Utils;
using SuperPuTTY.Scripting;

namespace SuperPuttyUnitTests
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class ScriptLifecycleTests
    {
        [Test]
        public void CancelPasswordPromptDoesNotReturnTerminalInput()
        {
            Assert.Throws<OperationCanceledException>(() => Commands.ShowScriptPrompt("Password", true,
                dialog => dialog.DialogResult = DialogResult.Cancel));
        }

        [Test]
        public void AcceptEmptyPasswordStillSendsEnter()
        {
            var command = Commands.ShowScriptPrompt("Password", true,
                dialog => dialog.DialogResult = DialogResult.OK);
            Assert.AreEqual("", command.Command);
            Assert.AreEqual(Keys.Enter, command.KeyData.KeyCode);
        }

        [TestCase("SLEEP 60000")]
        [TestCase("PWDPROMPT password")]
        [TestCase("PROMPT username")]
        public void ClosingSessionStopsWaitsAndPromptsBeforeLaterCommands(string first)
        {
            using (var panel = new ApplicationPanel(SuperPutty.Data.ConnectionProtocol.SSH))
            using (var entered = new ManualResetEventSlim())
            {
                int checks = 0;
                var target = new SPSL.ScriptTarget(panel.Handle, () =>
                {
                    if (Interlocked.Increment(ref checks) >= 3) entered.Set();
                    return !panel.ScriptsStopped;
                });
                int errors = 0;
                var worker = SPSL.CreateExecutionThread(new ExecuteScriptEventArgs { Targets = new[] { target } },
                    new[] { first, "UNSUPPORTED must never execute" }, line => errors++);
                worker.Start();
                try
                {
                    Assert.IsTrue(entered.Wait(2000), "Worker did not reach the wait/prompt.");
                    panel.Dispose();
                    Assert.IsTrue(worker.Join(2000), "Session close must interrupt the script promptly.");
                    Assert.AreEqual(0, errors, "Cancellation should neither execute later commands nor report an error.");
                }
                finally { panel.ScriptsStopped = true; worker.Join(2000); }
            }
        }

        [Test]
        public void InvalidTargetStopsBeforeOpeningPrompt()
        {
            int errors = 0;
            var worker = SPSL.CreateExecutionThread(new ExecuteScriptEventArgs(),
                new[] { "PWDPROMPT password" }, line => errors++);
            worker.Start();
            Assert.IsTrue(worker.Join(2000));
            Assert.AreEqual(0, errors);
        }

        [Test]
        public void TextDeliveryPreservesUnicodeAndHandlesLargeInput()
        {
            using (var window = new KeyWindow())
            {
                string text = new string('é', 12000) + "密碼";
                new CommandData(text).SendToTerminal(window.Handle);
                Assert.AreEqual(text, new string(window.Messages.ConvertAll(m => (char)m.WParam.ToInt32()).ToArray()));
            }
        }

        [Test]
        public void HungTerminalCannotStrandScriptWorker()
        {
            using (var ready = new ManualResetEventSlim())
            using (var close = new ManualResetEventSlim())
            {
                IntPtr handle = IntPtr.Zero;
                var host = new Thread(() =>
                {
                    using (var window = new KeyWindow())
                    {
                        handle = window.Handle;
                        ready.Set();
                        close.Wait(); // Deliberately do not pump window messages.
                    }
                }) { IsBackground = true };
                host.Start();
                try
                {
                    Assert.IsTrue(ready.Wait(2000));
                    int errorLine = 0;
                    var worker = SPSL.CreateExecutionThread(new ExecuteScriptEventArgs { Handle = handle },
                        new[] { "SENDLINE test", "SLEEP 60000" }, line => errorLine = line);
                    worker.Start();
                    Assert.IsTrue(worker.Join(2000));
                    Assert.AreEqual(1, errorLine);
                }
                finally { close.Set(); host.Join(2000); }
            }
        }

        [TestCase(Keys.Enter)]
        [TestCase(Keys.Alt | Keys.X)]
        [TestCase(Keys.Control | Keys.Shift | Keys.Alt | Keys.Left)]
        public void ScriptedKeysReachWindowWithBalancedPressesAndReleases(Keys keys)
        {
            using (var window = new KeyWindow())
            {
                new CommandData(new KeyEventArgs(keys)).SendToTerminal(window.Handle);
                NativeMessage native;
                while (PeekMessage(out native, window.Handle, 0, 0, 1)) DispatchMessage(ref native);
                var pressed = new HashSet<int>();
                bool primaryDown = false, primaryUp = false;
                foreach (var message in window.Messages)
                {
                    int key = message.WParam.ToInt32();
                    bool up = message.Msg == NativeMethods.WM_KEYUP || message.Msg == NativeMethods.WM_SYSKEYUP;
                    if (up) Assert.IsTrue(pressed.Remove(key), "Release must follow a press.");
                    else Assert.IsTrue(pressed.Add(key), "Key must only be pressed once.");
                    Assert.AreEqual(up, (message.LParam.ToInt64() & 0x80000000L) != 0);
                    if (key == (int)(keys & Keys.KeyCode))
                    {
                        primaryDown |= !up;
                        primaryUp |= up;
                        if ((keys & Keys.Alt) != 0)
                        {
                            Assert.IsTrue(pressed.Contains((int)Keys.Menu));
                            Assert.AreEqual(up ? NativeMethods.WM_SYSKEYUP : NativeMethods.WM_SYSKEYDOWN, message.Msg);
                            Assert.AreNotEqual(0, message.LParam.ToInt64() & (1 << 29));
                        }
                    }
                }
                Assert.IsTrue(primaryDown && primaryUp);
                Assert.IsEmpty(pressed);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr Window;
            public uint Message;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public System.Drawing.Point Point;
            public uint Private;
        }

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);
        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref NativeMessage message);

        private sealed class KeyWindow : NativeWindow, IDisposable
        {
            internal readonly List<Message> Messages = new List<Message>();
            internal KeyWindow() { CreateHandle(new CreateParams()); }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == NativeMethods.WM_CHAR || message.Msg == NativeMethods.WM_KEYDOWN || message.Msg == NativeMethods.WM_KEYUP
                    || message.Msg == NativeMethods.WM_SYSKEYDOWN || message.Msg == NativeMethods.WM_SYSKEYUP)
                {
                    Messages.Add(message);
                    return;
                }
                base.WndProc(ref message);
            }
            public void Dispose() { DestroyHandle(); }
        }
    }
}
