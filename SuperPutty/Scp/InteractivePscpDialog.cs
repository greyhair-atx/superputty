using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
using SuperPutty.Gui;
using SuperPutty.Utils;

namespace SuperPutty.Scp
{
    internal sealed class InteractivePscpResult
    {
        public int ExitCode = -1;
        public bool Cancelled = true;
        public string Error;
        public readonly List<string> Output = new List<string>();
    }

    /// <summary>
    /// Hosts a real console. PSCP owns all authentication I/O, including echo suppression.
    /// Only ordinary stdout (listings/progress) crosses the private pipe; console input is never read here.
    /// </summary>
    internal sealed class InteractivePscpDialog : Form
    {
        private const string HostSwitch = "--pscp-interactive-host";
        private const string PipePrefix = "SuperPuTTY-pscp-interactive-";
        private readonly Panel consolePanel = new Panel { Dock = DockStyle.Fill };
        private readonly Label status = new Label { Dock = DockStyle.Top, Height = 42 };
        private readonly Button cancel = new Button { Dock = DockStyle.Bottom, Text = "Cancel", Height = 30 };
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        private readonly object resources = new object();
        private readonly string executable, arguments;
        private readonly int timeoutMs;
        private readonly CancellationToken cancellation;
        private readonly Func<string, bool> outputHandler;
        private NamedPipeServerStream pipe;
        private SafeFileHandle job;
        private Process host;
        private IntPtr consoleWindow;
        private bool stopped, finished, workerEnded;
        private long lastActivity;

        internal InteractivePscpResult Result { get; } = new InteractivePscpResult();

        internal InteractivePscpDialog(string executable, string arguments, int timeoutMs,
            CancellationToken cancellation, Func<string, bool> outputHandler)
        {
            this.executable = executable;
            this.arguments = arguments;
            this.timeoutMs = timeoutMs;
            this.cancellation = cancellation;
            this.outputHandler = outputHandler;
            Text = "SCP authentication and transfer";
            Size = DpiUtils.ScaleSize(new System.Drawing.Size(850, 500));
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            status.Text = "Enter authentication responses in the console below.\r\nEach directory or transfer operation may prompt again.";
            Controls.Add(consolePanel);
            Controls.Add(status);
            Controls.Add(cancel);
            cancel.Click += delegate { Close(); };
            consolePanel.Resize += delegate { ResizeConsole(); };
            consolePanel.Enter += delegate { FocusConsole(); };
            Activated += delegate { FocusConsole(); };
            timer.Tick += CheckCancellationAndTimeout;
            ThemeManager.Apply(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Interlocked.Exchange(ref lastActivity, DateTime.UtcNow.Ticks);
            timer.Start();
            Task.Run((Action)RunOperation);
        }

        private void RunOperation()
        {
            string stage = "creating the console";
            try
            {
                string pipeName = PipePrefix + Guid.NewGuid().ToString("N");
                var security = new PipeSecurity();
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,
                    PipeAccessRights.FullControl, AccessControlType.Allow));
                lock (resources)
                {
                    if (stopped) return;
                    cancellation.ThrowIfCancellationRequested();
                    pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
                    job = Native.CreateJobObject(IntPtr.Zero, null);
                    if (job.IsInvalid) throw new Win32Exception();
                    var limits = new Native.ExtendedLimits();
                    limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    if (!Native.SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf(limits)))
                        throw new Win32Exception();
                    host = Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.SystemDirectory, "conhost.exe"),
                        Arguments = "-- " + CommandLineOptions.QuoteArgument(typeof(InteractivePscpDialog).Assembly.Location) +
                            " " + HostSwitch + " " + pipeName,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    // The helper waits for our command before starting PSCP. Cancellation can therefore
                    // always terminate the whole process tree, even during connection setup.
                    stage = "tracking the console process";
                    if (!Native.AssignProcessToJobObject(job, host.Handle))
                        throw new Win32Exception();
                }
                stage = "connecting to the console";
                var connected = pipe.BeginWaitForConnection(null, null);
                using (connected.AsyncWaitHandle)
                {
                    if (!connected.AsyncWaitHandle.WaitOne(15000))
                        throw new TimeoutException("The PSCP console could not be started.");
                    pipe.EndWaitForConnection(connected);
                }
                var reader = new BinaryReader(pipe, Encoding.UTF8, true);
                var writer = new BinaryWriter(pipe, Encoding.UTF8, true);
                stage = "reading the console window";
                var window = new IntPtr(reader.ReadInt64());
                Post(() => EmbedConsole(window));
                lock (resources)
                {
                    if (stopped) return;
                    writer.Write(executable);
                    writer.Write(arguments);
                    writer.Flush();
                }
                stage = "running PSCP";
                while (true)
                {
                    int kind = reader.ReadByte();
                    Interlocked.Exchange(ref lastActivity, DateTime.UtcNow.Ticks);
                    if (kind == 0)
                    {
                        string line = reader.ReadString();
                        Result.Output.Add(line);
                        if (outputHandler != null) outputHandler(line);
                    }
                    else if (kind == 1)
                    {
                        int exitCode = reader.ReadInt32();
                        Post(() => Complete(exitCode, null));
                        return;
                    }
                    else
                        throw new IOException("The PSCP console host stopped unexpectedly.");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is Win32Exception ||
                ex is InvalidOperationException || ex is OperationCanceledException || ex is TimeoutException)
            {
                // Never log the command, console transcript, responses or exception text from the child.
                string detail = ex is Win32Exception ? "Windows error " + ((Win32Exception)ex).NativeErrorCode : ex.GetType().Name;
                Post(() => Complete(-1, "SCP failed while " + stage + " (" + detail + "). Close and retry."));
            }
            finally
            {
                lock (resources)
                {
                    workerEnded = true;
                    if (stopped) ReleaseResources();
                }
            }
        }

        private void EmbedConsole(IntPtr window)
        {
            if (stopped) return;
            if (!Native.IsWindow(window))
            {
                StopOperation();
                Complete(-1, "The PSCP console window could not be located.");
                return;
            }
            consoleWindow = window;
            int style = Native.GetWindowLong(window, -16);
            Native.SetWindowLong(window, -16, ConsoleApplicationPanel.GetEmbeddedWindowStyle(style));
            Native.SetParent(window, consolePanel.Handle);
            if (Native.GetParent(window) != consolePanel.Handle)
            {
                StopOperation();
                Complete(-1, "The PSCP console could not be embedded.");
                return;
            }
            Native.SetWindowPos(window, IntPtr.Zero, 0, 0, consolePanel.Width, consolePanel.Height, 0x0020 | 0x0004);
            Native.ShowWindow(window, 5);
            ResizeConsole();
            FocusConsole();
        }

        private void ResizeConsole()
        {
            if (consoleWindow != IntPtr.Zero)
                Native.MoveWindow(consoleWindow, 0, 0, consolePanel.Width, consolePanel.Height, true);
        }

        private void FocusConsole()
        {
            if (consoleWindow == IntPtr.Zero || !Native.IsWindow(consoleWindow)) return;
            uint current = Native.GetWindowThreadProcessId(Handle, IntPtr.Zero);
            uint console = Native.GetWindowThreadProcessId(consoleWindow, IntPtr.Zero);
            bool attached = current != console && Native.AttachThreadInput(current, console, true);
            try { Native.SetFocus(consoleWindow); }
            finally { if (attached) Native.AttachThreadInput(current, console, false); }
        }

        private void CheckCancellationAndTimeout(object sender, EventArgs e)
        {
            if (cancellation.IsCancellationRequested) { Close(); return; }
            if (!finished && TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref lastActivity)).TotalMilliseconds >= timeoutMs)
            {
                StopOperation();
                Complete(-1, "SCP timed out waiting for authentication or transfer progress. Close and retry.");
            }
        }

        private void Complete(int exitCode, string error)
        {
            if (finished || IsDisposed) return;
            finished = true;
            timer.Stop();
            Result.ExitCode = exitCode;
            Result.Cancelled = false;
            Result.Error = error;
            if (exitCode == 0) { DialogResult = DialogResult.OK; Close(); }
            else
            {
                status.Text = error ?? "PSCP failed (exit code " + exitCode + "). Review the console output below.";
                cancel.Text = "Close";
            }
        }

        private void Post(Action action)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        private void StopOperation()
        {
            lock (resources)
            {
                if (stopped) return;
                stopped = true;
                if (job != null) job.Dispose();
                // Handles an assignment failure, before the helper was placed in our job.
                if (host != null)
                {
                    try { if (!host.HasExited) host.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                }
                if (workerEnded) ReleaseResources();
            }
        }

        private void ReleaseResources()
        {
            if (pipe != null) { pipe.Dispose(); pipe = null; }
            if (host != null) { host.Dispose(); host = null; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopOperation();
            timer.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopOperation();
                timer.Dispose();
                // Killing the job closes the remote pipe and releases any blocked read first.
                // The background worker owns the pipe until its read has returned.
            }
            base.Dispose(disposing);
        }

        internal static bool TryRunHost(string[] args)
        {
            if (args.Length != 2 || args[0] != HostSwitch) return false;
            Guid id;
            if (!args[1].StartsWith(PipePrefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(args[1].Substring(PipePrefix.Length), "N", out id)) return true;
            try
            {
                if (Native.GetConsoleWindow() == IntPtr.Zero && !Native.AttachConsole(0xFFFFFFFF) && !Native.AllocConsole())
                    return true;
                Native.SetConsoleTitle("SuperPuTTY SCP authentication");
                using (var connection = new NamedPipeClientStream(".", args[1], PipeDirection.InOut))
                {
                    connection.Connect(15000);
                    var reader = new BinaryReader(connection, Encoding.UTF8, true);
                    var writer = new BinaryWriter(connection, Encoding.UTF8, true);
                    writer.Write(Native.GetConsoleWindow().ToInt64());
                    writer.Flush();
                    string executable = reader.ReadString();
                    string arguments = reader.ReadString();
                    using (var child = Process.Start(new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = arguments,
                        WorkingDirectory = Path.GetDirectoryName(executable),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = false
                        // Stdin/stderr remain connected to the real console. In newer PSCP,
                        // authentication uses CONIN$/CONOUT$ directly; older versions use these handles.
                    }))
                    {
                        string line;
                        while ((line = child.StandardOutput.ReadLine()) != null)
                        {
                            writer.Write((byte)0);
                            writer.Write(line);
                            writer.Flush();
                        }
                        child.WaitForExit();
                        writer.Write((byte)1);
                        writer.Write(child.ExitCode);
                        writer.Flush();
                    }
                    // Keep failures visible until the user closes the dialog. The parent job
                    // terminates this helper when the dialog closes, including on app shutdown.
                    reader.ReadByte();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is Win32Exception || ex is InvalidOperationException || ex is TimeoutException)
            { }
            return true;
        }

        internal static string SanitizeExtraArguments(string arguments)
        {
            if (String.IsNullOrWhiteSpace(arguments)) return String.Empty;
            int count;
            IntPtr argv = Native.CommandLineToArgvW("pscp.exe " + arguments, out count);
            if (argv == IntPtr.Zero) throw new Win32Exception();
            try
            {
                var safe = new List<string>();
                for (int i = 1; i < count; i++)
                {
                    string arg = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                    string lower = arg.ToLowerInvariant();
                    if (lower == "-batch") continue;
                    if (lower == "-pw" || lower == "-pwfile" || lower == "-password" || lower == "/password" || lower == "/p")
                    { i++; continue; }
                    if (lower.StartsWith("-pw=") || lower.StartsWith("-pwfile=") || lower.StartsWith("/password:") || lower.StartsWith("/p:"))
                        continue;
                    safe.Add(CommandLineOptions.QuoteArgument(arg));
                }
                return String.Join(" ", safe);
            }
            finally { Native.LocalFree(argv); }
        }

        private static class Native
        {
            [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr CommandLineToArgvW(string command, out int count);
            [DllImport("kernel32.dll")] internal static extern IntPtr LocalFree(IntPtr memory);
            [StructLayout(LayoutKind.Sequential)] internal struct BasicLimits
            {
                internal long ProcessTime, JobTime;
                internal uint LimitFlags;
                internal UIntPtr MinWorkingSet, MaxWorkingSet;
                internal uint ActiveProcesses;
                internal UIntPtr Affinity;
                internal uint Priority, Scheduling;
            }
            [StructLayout(LayoutKind.Sequential)] internal struct IoCounters
            { internal ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
            [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimits
            {
                internal BasicLimits Basic;
                internal IoCounters Io;
                internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
            }
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool SetInformationJobObject(SafeFileHandle job, int info, ref ExtendedLimits limits, int size);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
            [DllImport("kernel32.dll")] internal static extern IntPtr GetConsoleWindow();
            [DllImport("kernel32.dll")] internal static extern bool AttachConsole(uint process);
            [DllImport("kernel32.dll")] internal static extern bool AllocConsole();
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern bool SetConsoleTitle(string title);
            [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
            [DllImport("user32.dll")] internal static extern IntPtr SetParent(IntPtr window, IntPtr parent);
            [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr window);
            [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window, int index);
            [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr window, int index, int value);
            [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, int flags);
            [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
            [DllImport("user32.dll")] internal static extern bool MoveWindow(IntPtr window, int x, int y, int w, int h, bool repaint);
            [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr window);
            [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);
            [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint first, uint second, bool attach);
        }
    }
}
