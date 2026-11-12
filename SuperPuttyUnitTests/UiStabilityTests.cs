using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using log4net.Appender;
using log4net.Core;
using NUnit.Framework;
using SuperPutty;
using SuperPutty.Data;

namespace SuperPuttyUnitTests
{
    [TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
    public class UiStabilityTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void LaunchProcessPollingCanOverlapPanelDisposal()
        {
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c set /p value=")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true
            }))
            using (var panel = new ConsoleApplicationPanel(ConnectionProtocol.WINCMD))
            using (var polled = new ManualResetEventSlim())
            {
                typeof(ConsoleApplicationPanel).GetField("launchProcess", PrivateInstance).SetValue(panel, process);
                var poll = typeof(ConsoleApplicationPanel).GetMethod("HasLaunchProcessExited", PrivateInstance);
                var worker = Task.Run(() =>
                {
                    for (int i = 0; i < 10000; i++)
                    {
                        poll.Invoke(panel, null);
                        polled.Set();
                    }
                });
                Assert.IsTrue(polled.Wait(2000));
                panel.Dispose();
                Assert.IsTrue(worker.Wait(5000), "Process polling failed to finish after disposal.");
                Assert.IsTrue((bool)poll.Invoke(panel, null));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnavailableProcessDuringCaptureClosesSessionWithoutEscaping(bool exited)
        {
            using (var panel = new ConsoleApplicationPanel(ConnectionProtocol.WINCMD))
            using (var process = new Process())
            {
                if (exited)
                {
                    process.StartInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c exit 0")
                    {
                        UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                    };
                    process.Start();
                    Assert.IsTrue(process.WaitForExit(5000));
                }
                int closed = 0;
                panel.m_CloseCallback = error => { Assert.IsTrue(error); closed++; };
                var resultType = typeof(ConsoleApplicationPanel).GetNestedType("CaptureResult", BindingFlags.NonPublic);
                var result = Activator.CreateInstance(resultType, true);
                resultType.GetProperty("Process").SetValue(result, process);
                resultType.GetProperty("WindowHandle").SetValue(result, IntPtr.Zero);
                Assert.DoesNotThrow(() => typeof(ConsoleApplicationPanel).GetMethod("CompleteCapture", PrivateInstance)
                    .Invoke(panel, new[] { result }));
                Assert.AreEqual(1, closed);
                Assert.AreEqual(SessionConnectionState.Disconnected, panel.ConnectionState);
                Assert.IsNull(typeof(ConsoleApplicationPanel).GetField("consoleProcess", PrivateInstance).GetValue(panel));
            }
        }

        [Test]
        public void ConsoleExitNotificationRunsOnTheUiThread()
        {
            using (var panel = new ConsoleApplicationPanel(ConnectionProtocol.WINCMD))
            {
                var handle = panel.Handle;
                int uiThread = Thread.CurrentThread.ManagedThreadId;
                int callbackThread = 0;
                panel.m_CloseCallback = error => callbackThread = Thread.CurrentThread.ManagedThreadId;
                Task.Run(() => typeof(ConsoleApplicationPanel).GetMethod("ConsoleProcessExited", PrivateInstance)
                    .Invoke(panel, new object[] { null, EventArgs.Empty })).GetAwaiter().GetResult();
                Assert.AreEqual(0, callbackThread, "The worker must queue the notification.");
                var elapsed = Stopwatch.StartNew();
                while (callbackThread == 0 && elapsed.ElapsedMilliseconds < 2000)
                {
                    Application.DoEvents();
                    Thread.Sleep(1);
                }
                Assert.AreEqual(uiThread, callbackThread);
            }
        }

        [Test]
        public void LogViewerKeepsEveryMessageWhileBackgroundLoggingContinues()
        {
            using (var viewer = new Log4netLogViewer())
            {
                try
                {
                    var appender = (AppenderSkeleton)typeof(Log4netLogViewer).GetField("memoryAppender", PrivateInstance).GetValue(viewer);
                    var tick = typeof(Log4netLogViewer).GetMethod("timerLogPull_Tick", PrivateInstance);
                    var text = (RichTextBox)typeof(Log4netLogViewer).GetField("richTextBoxLogMessages", PrivateInstance).GetValue(viewer);
                    string prefix = Guid.NewGuid().ToString("N") + "-";
                    var producer = Task.Run(() =>
                    {
                        for (int i = 0; i < 1000; i++)
                        {
                            appender.DoAppend(new LoggingEvent(typeof(UiStabilityTests), null, "test", Level.Info, prefix + i, null));
                            Thread.Yield();
                        }
                    });
                    var elapsed = Stopwatch.StartNew();
                    do { tick.Invoke(viewer, new object[] { null, EventArgs.Empty }); }
                    while (!producer.IsCompleted && elapsed.ElapsedMilliseconds < 10000);
                    Assert.IsTrue(producer.Wait(2000), "The background log producer did not finish.");
                    producer.GetAwaiter().GetResult();
                    tick.Invoke(viewer, new object[] { null, EventArgs.Empty });
                    var messages = text.Lines.Where(line => line.Contains(prefix)).Select(line => line.Substring(line.IndexOf(prefix))).ToArray();
                    CollectionAssert.AreEquivalent(Enumerable.Range(0, 1000).Select(i => prefix + i), messages);
                }
                finally
                {
                    typeof(Log4netLogViewer).GetMethod("DisposeLogger", PrivateInstance).Invoke(viewer, null);
                }
            }
        }

        private sealed class ScriptEditor : dlgScriptEditor
        {
            public string Error;
            protected override void ShowScriptFileError(string message) { Error = message; }
        }

        [Test]
        public void ScriptFileFailuresPreserveUnsavedEditorContents()
        {
            using (var editor = new ScriptEditor())
            {
                var text = (TextBox)typeof(dlgScriptEditor).GetField("textBoxSript", PrivateInstance).GetValue(editor);
                text.Text = "unsaved commands";
                string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "script.txt");
                Assert.IsFalse(editor.TryScriptFileOperation("load", () => text.AppendText(File.ReadAllText(missing))));
                StringAssert.Contains("Unable to load", editor.Error);
                Assert.AreEqual("unsaved commands", text.Text);
                Assert.IsFalse(editor.TryScriptFileOperation("save", () => File.WriteAllText(Path.GetTempPath(), text.Text)));
                StringAssert.Contains("Unable to save", editor.Error);
                Assert.AreEqual("unsaved commands", text.Text);
            }
        }
    }
}
