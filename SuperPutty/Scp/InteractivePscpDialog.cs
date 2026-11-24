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
        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(typeof(InteractivePscpDialog));
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
        private readonly ScpOutputCapture output;
        private NamedPipeServerStream pipe;
        private SafeFileHandle job;
        private Process host;
        private IntPtr consoleWindow;
        private uint consoleOwner;
        private uint consoleThread;
        private volatile bool stopped;
        private bool finished, workerEnded;
        private long lastActivity;

        internal InteractivePscpResult Result { get; } = new InteractivePscpResult();

        internal InteractivePscpDialog(string executable, string arguments, int timeoutMs,
            CancellationToken cancellation, Func<string, bool> outputHandler, bool preserveAllOutput = true)
        {
            this.executable = executable;
            this.arguments = arguments;
            this.timeoutMs = timeoutMs;
            this.cancellation = cancellation;
            this.outputHandler = outputHandler;
            this.output = new ScpOutputCapture(preserveAllOutput);
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
                // BinaryReader otherwise performs a pipe read for every frame kind
                // and string-length byte, particularly expensive for empty lines.
                lock (resources)
                {
                    if (stopped) return;
                    VerifyPipeClient(pipe, job);
                }
                var reader = new BinaryReader(new BufferedStream(pipe, 16 * 1024), Encoding.UTF8, true);
                var writer = new BinaryWriter(pipe, Encoding.UTF8, true);
                stage = "reading the console window";
                var window = new IntPtr(reader.ReadInt64());
                uint owner = reader.ReadUInt32();
                uint thread = reader.ReadUInt32();
                Post(() => EmbedConsole(window, owner, thread));
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
                        string line = ScpOutputCapture.ReadString(reader);
                        output.Add(line);
                        if (outputHandler != null) outputHandler(line);
                    }
                    else if (kind == 1)
                    {
                        int exitCode = reader.ReadInt32();
                        string[] captured = output.ToArray();
                        Post(() =>
                        {
                            Result.Output.AddRange(captured);
                            Complete(exitCode, null);
                        });
                        return;
                    }
                    else if (kind == 2)
                        throw new ScpOutputLimitException();
                    else
                        throw new IOException("The PSCP console host stopped unexpectedly.");
                }
            }
            catch (ScpOutputLimitException)
            {
                StopOperation();
                Post(() => Complete(-1, ScpOutputLimitException.SafeMessage));
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

        private static void VerifyPipeClient(NamedPipeServerStream connection, SafeFileHandle job)
        {
            uint clientId;
            if (!Native.GetNamedPipeClientProcessId(connection.SafePipeHandle, out clientId)) throw new Win32Exception();
            try
            {
                using (var client = Process.GetProcessById((int)clientId))
                {
                    bool inJob;
                    if (!Native.IsProcessInJob(client.Handle, job, out inJob)) throw new Win32Exception();
                    if (!inJob) throw new IOException("The PSCP console helper did not belong to its launch job.");
                }
            }
            catch (ArgumentException ex) { throw new IOException("The PSCP console helper exited before connection.", ex); }
        }

        internal static bool IsOwnedConsoleWindow(IntPtr window, uint expectedOwner, uint expectedThread, IntPtr expectedParent)
        {
            uint actualOwner;
            return window != IntPtr.Zero && expectedOwner != 0 && expectedThread != 0 &&
                NativeMethods.GetWindowThreadProcessId(window, out actualOwner) == expectedThread && actualOwner == expectedOwner &&
                (expectedParent == IntPtr.Zero || NativeMethods.GetAncestor(window, 1) == expectedParent);
        }

        private bool HasAttachedConsole()
        {
            return !stopped && consolePanel.IsHandleCreated &&
                IsOwnedConsoleWindow(consoleWindow, consoleOwner, consoleThread, consolePanel.Handle);
        }

        private void EmbedConsole(IntPtr window, uint owner, uint thread)
        {
            if (stopped) return;
            string stage = "verifying window ownership";
            try
            {
                IntPtr parent = NativeMethods.GetAncestor(window, 1);
                if (!IsOwnedConsoleWindow(window, owner, thread, IntPtr.Zero) ||
                    (parent != IntPtr.Zero && parent != Native.GetDesktopWindow()))
                    throw new InvalidOperationException("The PSCP console window is no longer available to embed.");

                stage = "attaching the window";
                ApplicationPanel.ReparentWindow(window, consolePanel.Handle);
                if (!IsOwnedConsoleWindow(window, owner, thread, consolePanel.Handle))
                    throw new InvalidOperationException("The embedded PSCP console window changed.");

                const int styleIndex = -16;
                stage = "setting the window frame";
                NativeMethods.ClearLastError(0);
                int style = NativeMethods.GetWindowLong(window, styleIndex);
                if (style == 0 && Marshal.GetLastWin32Error() != 0) throw new Win32Exception();
                NativeMethods.ClearLastError(0);
                int previous = NativeMethods.SetWindowLong(window, styleIndex, ConsoleApplicationPanel.GetEmbeddedWindowStyle(style));
                if (previous == 0 && Marshal.GetLastWin32Error() != 0) throw new Win32Exception();
                if (!NativeMethods.SetWindowPos(window, IntPtr.Zero, 0, 0, consolePanel.Width, consolePanel.Height, 0x0020 | 0x0004))
                    throw new Win32Exception();
                consoleWindow = window;
                consoleOwner = owner;
                consoleThread = thread;
                stage = "verifying the embedded window";
                if (!HasAttachedConsole()) throw new InvalidOperationException("The embedded PSCP console window changed.");
                Native.ShowWindow(window, 5);
                FocusConsole();
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                Log.Warn("The PSCP console could not be embedded.", ex);
                consoleWindow = IntPtr.Zero;
                StopOperation();
                Complete(-1, "The PSCP console could not be embedded while " + stage + ". Close and retry.");
            }
        }

        private void ResizeConsole()
        {
            if (HasAttachedConsole())
                Native.MoveWindow(consoleWindow, 0, 0, consolePanel.Width, consolePanel.Height, true);
        }

        private void FocusConsole()
        {
            if (!IsHandleCreated || !HasAttachedConsole()) return;
            uint current = Native.GetWindowThreadProcessId(Handle, IntPtr.Zero);
            uint console = Native.GetWindowThreadProcessId(consoleWindow, IntPtr.Zero);
            bool attached = current != 0 && console != 0 && current != console && Native.AttachThreadInput(current, console, true);
            try { if (HasAttachedConsole()) Native.SetFocus(consoleWindow); }
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
                    // Windows may give the console window to a different process
                    // than the conhost launcher. Report the identity of this
                    // helper's own console over the job-verified connection.
                    IntPtr window = Native.GetConsoleWindow();
                    uint owner;
                    uint windowThread = NativeMethods.GetWindowThreadProcessId(window, out owner);
                    writer.Write(window.ToInt64());
                    writer.Write(owner);
                    writer.Write(windowThread);
                    writer.Flush();
                    string executable = ScpOutputCapture.ReadString(reader);
                    string arguments = ScpOutputCapture.ReadString(reader);
                    using (var output = new BufferedOutputWriter(connection))
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
                        try
                        {
                            string line;
                            while ((line = ScpOutputCapture.ReadLine(child.StandardOutput)) != null)
                                output.WriteLine(line);
                            child.WaitForExit();
                            output.WriteExit(child.ExitCode);
                        }
                        catch (ScpOutputLimitException)
                        {
                            // A fixed status crosses the pipe; no oversized content is forwarded.
                            output.WriteLimit();
                        }
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
            return WindowsCommandLine.RewriteSensitive(arguments, null, true, true);
        }

        /// <summary>
        /// Preserves the wire framing while coalescing short lines into pipe writes.
        /// The first line is immediate; a timer also flushes quiet producers whose
        /// next ReadLine may block indefinitely waiting for authentication or work.
        /// </summary>
        internal sealed class BufferedOutputWriter : IDisposable
        {
            private const int BatchBytes = 16 * 1024;
            private const int FlushIntervalMilliseconds = 100;
            private readonly object gate = new object();
            private readonly Stream destination;
            private readonly MemoryStream pending = new MemoryStream();
            private readonly BinaryWriter frames;
            private readonly System.Threading.Timer flushTimer;
            private Exception failure;
            private bool firstLine = true, disposed;

            internal BufferedOutputWriter(Stream destination)
            {
                this.destination = destination;
                frames = new BinaryWriter(pending, Encoding.UTF8, true);
                flushTimer = new System.Threading.Timer(FlushPending, null,
                    FlushIntervalMilliseconds, FlushIntervalMilliseconds);
            }

            internal void WriteLine(string line)
            {
                if (line.Length > ScpOutputCapture.MaximumLineCharacters) throw new ScpOutputLimitException();
                lock (gate)
                {
                    ThrowIfUnavailable();
                    frames.Write((byte)0);
                    frames.Write(line);
                    if (firstLine || pending.Length >= BatchBytes) Flush();
                    firstLine = false;
                }
            }

            internal void WriteExit(int exitCode)
            {
                lock (gate)
                {
                    ThrowIfUnavailable();
                    frames.Write((byte)1);
                    frames.Write(exitCode);
                    Flush();
                }
            }

            internal void WriteLimit()
            {
                lock (gate)
                {
                    ThrowIfUnavailable();
                    frames.Write((byte)2);
                    Flush();
                }
            }

            private void ThrowIfUnavailable()
            {
                if (disposed) throw new ObjectDisposedException(nameof(BufferedOutputWriter));
                if (failure != null) throw new IOException("The PSCP output pipe is unavailable.", failure);
            }

            private void Flush()
            {
                if (pending.Length == 0) return;
                destination.Write(pending.GetBuffer(), 0, checked((int)pending.Length));
                destination.Flush();
                pending.SetLength(0);
                pending.Position = 0;
            }

            private void FlushPending(object state)
            {
                lock (gate)
                {
                    if (disposed || failure != null) return;
                    try { Flush(); }
                    catch (Exception ex) when (ex is IOException || ex is InvalidOperationException)
                    {
                        // A disconnected parent must not raise an unhandled timer
                        // exception. The producer observes this on its next frame.
                        failure = ex;
                    }
                }
            }

            public void Dispose()
            {
                lock (gate)
                {
                    if (disposed) return;
                    disposed = true;
                    flushTimer.Dispose();
                    frames.Dispose();
                    pending.Dispose();
                }
                // Completed/status frames flush explicitly. On a failed or cancelled
                // producer, discard a partial batch instead of masking its exception.
            }
        }

        private static class Native
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsProcessInJob(IntPtr process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool inJob);
            [DllImport("user32.dll")] internal static extern IntPtr GetDesktopWindow();
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
            [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
            [DllImport("user32.dll")] internal static extern bool MoveWindow(IntPtr window, int x, int y, int w, int h, bool repaint);
            [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr window);
            [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);
            [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint first, uint second, bool attach);
        }
    }
}
