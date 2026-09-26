using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;
using SuperPutty.Utils;
using SuperPuTTY.Scripting;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class SessionCoordinationTests
    {
        private sealed class Terminal : NativeWindow, IDisposable
        {
            internal readonly StringBuilder Text = new StringBuilder();
            internal Terminal() { CreateHandle(new CreateParams()); }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == NativeMethods.WM_CHAR) { Text.Append((char)message.WParam.ToInt32()); return; }
                base.WndProc(ref message);
            }
            public void Dispose() { DestroyHandle(); }
        }

        private static void PumpUntil(Func<bool> done)
        {
            var elapsed = Stopwatch.StartNew();
            while (!done() && elapsed.ElapsedMilliseconds < 5000)
            {
                Application.DoEvents();
                Thread.Sleep(1);
            }
            Assert.IsTrue(done(), "Operation did not finish while the receiver pumped messages.");
        }

        [Test]
        public void OverlappingScriptsKeepTheirEntireInputTogether()
        {
            using (var terminal = new Terminal())
            {
                int errors = 0;
                var args = new ExecuteScriptEventArgs { Handle = terminal.Handle };
                var first = SPSL.CreateExecutionThread(args, new[] { "SENDCHAR AA", "SLEEP 200", "SENDCHAR aa" }, line => Interlocked.Increment(ref errors));
                var second = SPSL.CreateExecutionThread(args, new[] { "SENDCHAR BB", "SLEEP 1", "SENDCHAR bb" }, line => Interlocked.Increment(ref errors));
                first.Start();
                PumpUntil(() => terminal.Text.Length == 2);
                second.Start();
                Assert.Throws<InvalidOperationException>(() => new CommandData("toolbar").SendToTerminal(terminal.Handle));
                PumpUntil(() => !first.IsAlive && !second.IsAlive);
                Assert.AreEqual("AAaaBBbb", terminal.Text.ToString());
                Assert.AreEqual(0, errors);
                new CommandData("after").SendToTerminal(terminal.Handle);
                Assert.AreEqual("AAaaBBbbafter", terminal.Text.ToString(), "Reservations must be released.");
            }
        }

        [Test]
        public void OppositeBroadcastOrdersDoNotDeadlock()
        {
            using (var one = new Terminal())
            using (var two = new Terminal())
            {
                var a = new SPSL.ScriptTarget(one.Handle);
                var b = new SPSL.ScriptTarget(two.Handle);
                int errors = 0;
                var first = SPSL.CreateExecutionThread(new ExecuteScriptEventArgs { Targets = new[] { a, b } },
                    new[] { "SENDCHAR A", "SLEEP 50", "SENDCHAR a" }, line => Interlocked.Increment(ref errors));
                var second = SPSL.CreateExecutionThread(new ExecuteScriptEventArgs { Targets = new[] { b, a } },
                    new[] { "SENDCHAR B", "SLEEP 50", "SENDCHAR b" }, line => Interlocked.Increment(ref errors));
                first.Start(); second.Start();
                PumpUntil(() => !first.IsAlive && !second.IsAlive);
                Assert.AreEqual(0, errors);
                Assert.That(one.Text.ToString(), Is.EqualTo("AaBb").Or.EqualTo("BbAa"));
                Assert.AreEqual(one.Text.ToString(), two.Text.ToString());
            }
        }

        [Test]
        public void ClosingTargetCancelsActiveAndQueuedScripts()
        {
            using (var terminal = new Terminal())
            {
                bool alive = true;
                var target = new SPSL.ScriptTarget(terminal.Handle, () => Volatile.Read(ref alive));
                var args = new ExecuteScriptEventArgs { Targets = new[] { target } };
                int errors = 0;
                var first = SPSL.CreateExecutionThread(args, new[] { "SENDCHAR A", "SLEEP 60000" }, line => errors++);
                var second = SPSL.CreateExecutionThread(args, new[] { "SENDCHAR B" }, line => errors++);
                first.Start();
                PumpUntil(() => terminal.Text.Length == 1);
                second.Start();
                Volatile.Write(ref alive, false);
                Assert.IsTrue(first.Join(2000)); Assert.IsTrue(second.Join(2000));
                Assert.AreEqual("A", terminal.Text.ToString());
                Assert.AreEqual(0, errors);
                new CommandData("released").SendToTerminal(terminal.Handle);
                Assert.AreEqual("Areleased", terminal.Text.ToString());
            }
        }

        [Test]
        public void DisjointSessionsCanRunConcurrently()
        {
            using (var one = new Terminal())
            using (var two = new Terminal())
            {
                bool alive = true;
                var first = SPSL.CreateExecutionThread(new ExecuteScriptEventArgs {
                    Targets = new[] { new SPSL.ScriptTarget(one.Handle, () => Volatile.Read(ref alive)) } },
                    new[] { "SENDCHAR A", "SLEEP 60000" });
                first.Start();
                try
                {
                    PumpUntil(() => one.Text.Length == 1);
                    var second = SPSL.CreateExecutionThread(new ExecuteScriptEventArgs { Handle = two.Handle }, new[] { "SENDCHAR B" });
                    second.Start();
                    PumpUntil(() => !second.IsAlive);
                    Assert.AreEqual("B", two.Text.ToString());
                    Assert.IsTrue(first.IsAlive);
                }
                finally { Volatile.Write(ref alive, false); Assert.IsTrue(first.Join(2000)); }
            }
        }

        [Test]
        public void ScriptFailureReleasesSessionReservation()
        {
            using (var terminal = new Terminal())
            {
                int errorLine = 0;
                var worker = SPSL.CreateExecutionThread(new ExecuteScriptEventArgs { Handle = terminal.Handle },
                    new[] { "UNSUPPORTED" }, line => errorLine = line);
                worker.Start();
                Assert.IsTrue(worker.Join(2000));
                Assert.AreEqual(1, errorLine);
                new CommandData("after error").SendToTerminal(terminal.Handle);
                Assert.AreEqual("after error", terminal.Text.ToString());
            }
        }

        [TestCase("bad:name")]
        [TestCase("..\\outside")]
        [TestCase("C:\\outside")]
        [TestCase("../outside")]
        [TestCase("..")]
        [TestCase("name.")]
        [TestCase("name ")]
        [TestCase("CON")]
        [TestCase("NUL.txt")]
        [TestCase("COM1")]
        [TestCase("LPT2")]
        [TestCase("")]
        public void InvalidLayoutNamesAreRejected(string name)
        {
            string error;
            Assert.IsFalse(LayoutData.TryValidateName(name, out error));
            Assert.IsNotEmpty(error);
            Assert.Throws<ArgumentException>(() => SuperPutty.SuperPuTTY.RenameLayout(new LayoutData("unused.xml"), name, () => Assert.Fail("Must not save settings.")));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DefaultLayoutRenamePersistsOrRollsBackOnSaveFailure(bool failSave)
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPuttyLayoutTest-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            string before = Path.Combine(directory, "Before.xml"), after = Path.Combine(directory, "After.xml");
            string oldDefault = SuperPutty.SuperPuTTY.Settings.DefaultLayoutName;
            try
            {
                File.WriteAllText(before, "<layout/>");
                var layout = new LayoutData(before);
                SuperPutty.SuperPuTTY.Settings.DefaultLayoutName = "Before";
                string persistedName = null;
                Action rename = () => SuperPutty.SuperPuTTY.RenameLayout(layout, "After", () =>
                {
                    persistedName = SuperPutty.SuperPuTTY.Settings.DefaultLayoutName;
                    if (failSave) throw new IOException("Test settings failure.");
                });
                if (failSave) Assert.Throws<IOException>(() => rename()); else rename();
                Assert.AreEqual("After", persistedName);
                Assert.AreEqual(failSave ? "Before" : "After", SuperPutty.SuperPuTTY.Settings.DefaultLayoutName);
                Assert.IsTrue(layout.IsDefault);
                Assert.AreEqual(failSave ? before : after, layout.FilePath);
                Assert.IsTrue(File.Exists(failSave ? before : after));
                Assert.IsFalse(File.Exists(failSave ? after : before));
            }
            finally
            {
                SuperPutty.SuperPuTTY.Settings.DefaultLayoutName = oldDefault;
                if (File.Exists(before)) File.Delete(before);
                if (File.Exists(after)) File.Delete(after);
                Directory.Delete(directory);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RdpStartupCallbackWaitsForLoginAndRunsOnlyOnce(bool closeBeforeLogin)
        {
            RdpClientPanel panel;
            if (!RdpClientPanel.TryCreate(new SessionData { Proto = ConnectionProtocol.RDP, Host = "127.0.0.1" }, null, out panel))
                Assert.Ignore("Microsoft RDP ActiveX control is unavailable.");
            using (panel)
            {
                Assert.IsTrue(panel.ExternalProcessCaptured, "The window must remain available for display handling.");
                Assert.IsFalse(panel.ScriptInputReady);
                int calls = 0;
                panel.WhenCaptured(() => calls++);
                Assert.AreEqual(0, calls);
                var login = typeof(RdpClientPanel).GetMethod("Client_OnLoginComplete", BindingFlags.Instance | BindingFlags.NonPublic);
                if (closeBeforeLogin)
                {
                    panel.Dispose();
                    login.Invoke(panel, new object[] { panel, EventArgs.Empty });
                    Assert.AreEqual(0, calls);
                    Assert.IsFalse(panel.ScriptInputReady);
                    return;
                }
                login.Invoke(panel, new object[] { panel, EventArgs.Empty });
                Assert.IsTrue(panel.ScriptInputReady);
                Assert.AreEqual(1, calls);
                login.Invoke(panel, new object[] { panel, EventArgs.Empty });
                Assert.AreEqual(1, calls);
                panel.WhenCaptured(() => calls++);
                Assert.AreEqual(2, calls, "Scripts loaded after login should start immediately.");
                panel.Dispose();
                Assert.IsFalse(panel.ScriptInputReady);
            }
        }
    }
}
