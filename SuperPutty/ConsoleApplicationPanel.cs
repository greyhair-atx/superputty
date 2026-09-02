/*
 * Copyright (c) 2026 SuperPuTTY contributors
 * Licensed under the MIT license. See License.txt in the project root.
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using log4net;
using SuperPutty.Data;
using SuperPutty.Utils;

namespace SuperPutty
{
    /// <summary>
    /// Hosts classic Win32 console windows without sharing PuTTY's process,
    /// window-discovery, style, focus, resize, or shutdown state.
    /// </summary>
    public sealed class ConsoleApplicationPanel : ApplicationPanel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ConsoleApplicationPanel));
        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

        private readonly ConnectionProtocol protocol;
        private readonly CancellationTokenSource captureCancellation = new CancellationTokenSource();
        private Process launchProcess;
        private Process consoleProcess;
        private Thread captureThread;
        private IntPtr consoleWindow;
        private bool started;
        private bool closing;
        private int closeReported;

        public ConsoleApplicationPanel(ConnectionProtocol protocol)
            : base(protocol)
        {
            if (!Supports(protocol))
            {
                throw new ArgumentOutOfRangeException("protocol", protocol, "Only WinCMD and PowerShell are console protocols.");
            }

            this.protocol = protocol;
        }

        protected override bool UsesManagedChildHost { get { return true; } }

        public override IntPtr AppWindowHandle { get { return this.consoleWindow; } }

        public override bool ExternalProcessCaptured
        {
            get { return this.consoleWindow != IntPtr.Zero && ConsoleNativeMethods.IsWindow(this.consoleWindow); }
        }

        internal static bool Supports(ConnectionProtocol protocol)
        {
            return protocol == ConnectionProtocol.WINCMD || protocol == ConnectionProtocol.PS;
        }

        internal static int GetEmbeddedWindowStyle(int style)
        {
            int frameStyles = ConsoleNativeMethods.WS_CAPTION |
                ConsoleNativeMethods.WS_BORDER |
                ConsoleNativeMethods.WS_DLGFRAME |
                ConsoleNativeMethods.WS_THICKFRAME |
                ConsoleNativeMethods.WS_SYSMENU |
                ConsoleNativeMethods.WS_MINIMIZEBOX |
                ConsoleNativeMethods.WS_MAXIMIZEBOX |
                ConsoleNativeMethods.WS_POPUP;
            return (style & ~frameStyles) | ConsoleNativeMethods.WS_CHILD;
        }

        public override void RefreshAppWindow()
        {
            this.MoveConsoleWindow();
        }

        public override bool ReFocusPuTTY(string caller)
        {
            if (!this.ExternalProcessCaptured || !this.Visible)
            {
                return false;
            }

            this.MoveConsoleWindow();
            ConsoleNativeMethods.ShowWindow(this.consoleWindow, ConsoleNativeMethods.SW_SHOW);
            ConsoleNativeMethods.BringWindowToTop(this.consoleWindow);

            uint panelThread = ConsoleNativeMethods.GetWindowThreadProcessId(this.Handle, IntPtr.Zero);
            uint consoleThread = ConsoleNativeMethods.GetWindowThreadProcessId(this.consoleWindow, IntPtr.Zero);
            bool attached = false;
            bool focused;
            try
            {
                if (panelThread != 0 && consoleThread != 0 && panelThread != consoleThread)
                {
                    attached = ConsoleNativeMethods.AttachThreadInput(panelThread, consoleThread, true);
                }

                ConsoleNativeMethods.SetFocus(this.consoleWindow);
                focused = ConsoleNativeMethods.GetFocus() == this.consoleWindow;
            }
            finally
            {
                if (attached)
                {
                    ConsoleNativeMethods.AttachThreadInput(panelThread, consoleThread, false);
                }
            }

            Log.DebugFormat("[{0}] Console focus requested by {1}; result={2}",
                this.consoleWindow, caller, focused);
            return focused;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            if (!this.started && this.Visible && !String.IsNullOrEmpty(this.ApplicationName))
            {
                this.StartConsole();
            }
            else if (this.Visible && this.ExternalProcessCaptured)
            {
                this.MoveConsoleWindow();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.MoveConsoleWindow();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            this.CloseConsole();
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.CloseConsole();
                bool sameProcess = Object.ReferenceEquals(this.consoleProcess, this.launchProcess);
                this.DisposeProcess(ref this.consoleProcess);
                if (sameProcess)
                {
                    this.launchProcess = null;
                }
                else
                {
                    this.DisposeProcess(ref this.launchProcess);
                }
            }

            base.Dispose(disposing);
        }

        private void StartConsole()
        {
            this.started = true;
            try
            {
                if (!File.Exists(this.ApplicationName))
                {
                    throw new FileNotFoundException("Console host was not found.", this.ApplicationName);
                }

                HashSet<IntPtr> existingWindows = CaptureTopLevelWindows();
                Process process = new Process
                {
                    StartInfo =
                    {
                        FileName = this.ApplicationName,
                        Arguments = this.ApplicationParameters,
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Maximized
                    }
                };

                if (!String.IsNullOrEmpty(this.ApplicationWorkingDirectory) &&
                    Directory.Exists(this.ApplicationWorkingDirectory))
                {
                    process.StartInfo.WorkingDirectory = this.ApplicationWorkingDirectory;
                }

                process.Start();
                this.launchProcess = process;

                string clientExecutable = PuttyStartInfo.GetConsoleClientExecutable(this.protocol);
                CaptureRequest request = new CaptureRequest
                {
                    LaunchProcessId = process.Id,
                    ExistingWindows = existingWindows,
                    ExpectedProcessName = Path.GetFileNameWithoutExtension(clientExecutable),
                    DeadlineUtc = DateTime.UtcNow.Add(CaptureTimeout)
                };

                this.captureThread = new Thread(() => this.CaptureConsoleWindow(request))
                {
                    IsBackground = true,
                    Name = "SuperPuTTY console window capture"
                };
                this.captureThread.Start();
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
            {
                Log.Error("Unable to launch the console host.", ex);
                MessageBox.Show(this, ex.Message, "Console Launch Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.NotifyClosed(true);
            }
        }

        private void CaptureConsoleWindow(CaptureRequest request)
        {
            CaptureResult result = null;
            while (!this.captureCancellation.IsCancellationRequested && DateTime.UtcNow < request.DeadlineUtc)
            {
                result = FindNewConsoleWindow(request);
                if (result != null)
                {
                    break;
                }

                if (this.HasLaunchProcessExited())
                {
                    break;
                }

                if (this.captureCancellation.Token.WaitHandle.WaitOne(50))
                {
                    return;
                }
            }

            if (this.captureCancellation.IsCancellationRequested)
            {
                if (result != null)
                {
                    result.Process.Dispose();
                }
                return;
            }

            this.RunOnUiThread(() => this.CompleteCapture(result));
        }

        private void CompleteCapture(CaptureResult result)
        {
            if (this.closing || this.IsDisposed || this.Disposing)
            {
                if (result != null)
                {
                    result.Process.Dispose();
                }
                return;
            }

            if (result == null)
            {
                Log.Error("No new ConsoleWindowClass window was found for the launched console process tree.");
                MessageBox.Show(this,
                    "The console window could not be located and was not embedded.",
                    "Console Window Not Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                this.CloseConsole();
                this.NotifyClosed(true);
                return;
            }

            this.consoleProcess = result.Process;
            this.consoleWindow = result.WindowHandle;
            this.consoleProcess.EnableRaisingEvents = true;
            this.consoleProcess.Exited += this.ConsoleProcessExited;

            if (!this.AttachConsoleWindow())
            {
                this.CloseConsole();
                this.NotifyClosed(true);
                return;
            }

            Log.InfoFormat("Captured {0} console window. Process={1}, Handle={2}",
                this.protocol, this.consoleProcess.ProcessName, this.consoleWindow);
            this.MoveConsoleWindow();
            this.BeginInvoke(new MethodInvoker(delegate { this.ReFocusPuTTY("ConsoleCaptureCompleted"); }));
        }

        private bool AttachConsoleWindow()
        {
            int style = ConsoleNativeMethods.GetWindowLong(this.consoleWindow, ConsoleNativeMethods.GWL_STYLE);
            ConsoleNativeMethods.SetWindowLong(
                this.consoleWindow,
                ConsoleNativeMethods.GWL_STYLE,
                GetEmbeddedWindowStyle(style));

            ConsoleNativeMethods.SetLastError(0);
            ConsoleNativeMethods.SetParent(this.consoleWindow, this.Handle);
            int error = Marshal.GetLastWin32Error();
            if (ConsoleNativeMethods.GetParent(this.consoleWindow) != this.Handle)
            {
                Log.ErrorFormat("Unable to parent console window {0} to panel {1}. Win32Error={2}",
                    this.consoleWindow, this.Handle, error);
                MessageBox.Show(this,
                    "The console window could not be embedded.",
                    "Console Window Capture Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            ConsoleNativeMethods.SetWindowPos(
                this.consoleWindow,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                ConsoleNativeMethods.SWP_NOMOVE |
                ConsoleNativeMethods.SWP_NOSIZE |
                ConsoleNativeMethods.SWP_NOZORDER |
                ConsoleNativeMethods.SWP_NOACTIVATE |
                ConsoleNativeMethods.SWP_FRAMECHANGED);
            ConsoleNativeMethods.ShowWindow(this.consoleWindow, ConsoleNativeMethods.SW_SHOW);
            return true;
        }

        private void MoveConsoleWindow()
        {
            if (!this.ExternalProcessCaptured || !this.Visible || this.Width <= 0 || this.Height <= 0)
            {
                return;
            }

            ConsoleNativeMethods.MoveWindow(this.consoleWindow, 0, 0, this.Width, this.Height, true);
        }

        private void CloseConsole()
        {
            if (this.closing)
            {
                return;
            }

            this.closing = true;
            this.captureCancellation.Cancel();

            IntPtr window = this.consoleWindow;
            this.consoleWindow = IntPtr.Zero;
            if (window != IntPtr.Zero && ConsoleNativeMethods.IsWindow(window))
            {
                ConsoleNativeMethods.PostMessage(window, ConsoleNativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            else
            {
                this.TerminateUncapturedLaunchProcess();
            }
        }

        private void ConsoleProcessExited(object sender, EventArgs e)
        {
            if (!this.closing)
            {
                this.NotifyClosed(true);
            }
        }

        private void NotifyClosed(bool error)
        {
            if (Interlocked.Exchange(ref this.closeReported, 1) == 0 && this.m_CloseCallback != null)
            {
                this.m_CloseCallback(error);
            }
        }

        private bool HasLaunchProcessExited()
        {
            try
            {
                return this.launchProcess == null || this.launchProcess.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private void RunOnUiThread(Action action)
        {
            if (this.IsDisposed || this.Disposing || !this.IsHandleCreated)
            {
                return;
            }

            try
            {
                this.BeginInvoke(new MethodInvoker(delegate
                {
                    if (!this.IsDisposed && !this.Disposing)
                    {
                        action();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // The panel was destroyed after capture completed.
            }
        }

        private static HashSet<IntPtr> CaptureTopLevelWindows()
        {
            HashSet<IntPtr> windows = new HashSet<IntPtr>();
            ConsoleNativeMethods.EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                windows.Add(window);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private static CaptureResult FindNewConsoleWindow(CaptureRequest request)
        {
            CaptureResult result = null;
            ConsoleNativeMethods.EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                if (request.ExistingWindows.Contains(window))
                {
                    return true;
                }

                StringBuilder className = new StringBuilder(64);
                if (ConsoleNativeMethods.GetClassName(window, className, className.Capacity) == 0 ||
                    !String.Equals(className.ToString(), "ConsoleWindowClass", StringComparison.Ordinal))
                {
                    return true;
                }

                uint processId;
                ConsoleNativeMethods.GetWindowThreadProcessId(window, out processId);
                if (processId == 0 || !IsDescendantProcess(processId, (uint)request.LaunchProcessId))
                {
                    return true;
                }

                Process process = null;
                try
                {
                    process = Process.GetProcessById((int)processId);
                    if (!String.Equals(process.ProcessName, request.ExpectedProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Dispose();
                        return true;
                    }

                    result = new CaptureResult { Process = process, WindowHandle = window };
                    return false;
                }
                catch (ArgumentException)
                {
                    if (process != null)
                    {
                        process.Dispose();
                    }
                    return true;
                }
                catch (InvalidOperationException)
                {
                    if (process != null)
                    {
                        process.Dispose();
                    }
                    return true;
                }
            }, IntPtr.Zero);
            return result;
        }

        private static bool IsDescendantProcess(uint processId, uint ancestorProcessId)
        {
            IntPtr snapshot = ConsoleNativeMethods.CreateToolhelp32Snapshot(
                ConsoleNativeMethods.TH32CS_SNAPPROCESS, 0);
            if (snapshot == ConsoleNativeMethods.INVALID_HANDLE_VALUE)
            {
                return false;
            }

            try
            {
                Dictionary<uint, uint> parents = new Dictionary<uint, uint>();
                ConsoleNativeMethods.PROCESSENTRY32 entry = new ConsoleNativeMethods.PROCESSENTRY32
                {
                    dwSize = (uint)Marshal.SizeOf(typeof(ConsoleNativeMethods.PROCESSENTRY32))
                };
                if (ConsoleNativeMethods.Process32First(snapshot, ref entry))
                {
                    do
                    {
                        parents[entry.th32ProcessID] = entry.th32ParentProcessID;
                    }
                    while (ConsoleNativeMethods.Process32Next(snapshot, ref entry));
                }

                HashSet<uint> visited = new HashSet<uint>();
                uint current = processId;
                while (current != 0 && visited.Add(current))
                {
                    uint parent;
                    if (!parents.TryGetValue(current, out parent))
                    {
                        return false;
                    }
                    if (parent == ancestorProcessId)
                    {
                        return true;
                    }
                    current = parent;
                }
                return false;
            }
            finally
            {
                ConsoleNativeMethods.CloseHandle(snapshot);
            }
        }

        private void DisposeProcess(ref Process process)
        {
            Process value = process;
            process = null;
            if (value != null)
            {
                value.Dispose();
            }
        }

        private void TerminateUncapturedLaunchProcess()
        {
            Process process = this.launchProcess;
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // The launch process exited while the panel was closing.
            }
            catch (Win32Exception ex)
            {
                Log.Warn("Unable to stop an uncaptured console host.", ex);
            }
        }

        private sealed class CaptureRequest
        {
            public int LaunchProcessId { get; set; }
            public HashSet<IntPtr> ExistingWindows { get; set; }
            public string ExpectedProcessName { get; set; }
            public DateTime DeadlineUtc { get; set; }
        }

        private sealed class CaptureResult
        {
            public Process Process { get; set; }
            public IntPtr WindowHandle { get; set; }
        }

        private static class ConsoleNativeMethods
        {
            internal const int GWL_STYLE = -16;
            internal const int WS_CAPTION = 0x00C00000;
            internal const int WS_BORDER = 0x00800000;
            internal const int WS_HSCROLL = 0x00100000;
            internal const int WS_VSCROLL = 0x00200000;
            internal const int WS_DLGFRAME = 0x00400000;
            internal const int WS_THICKFRAME = 0x00040000;
            internal const int WS_SYSMENU = 0x00080000;
            internal const int WS_MINIMIZEBOX = 0x00020000;
            internal const int WS_MAXIMIZEBOX = 0x00010000;
            internal const int WS_CHILD = 0x40000000;
            internal const int WS_POPUP = unchecked((int)0x80000000);
            internal const int SWP_NOSIZE = 0x0001;
            internal const int SWP_NOMOVE = 0x0002;
            internal const int SWP_NOZORDER = 0x0004;
            internal const int SWP_NOACTIVATE = 0x0010;
            internal const int SWP_FRAMECHANGED = 0x0020;
            internal const int SW_SHOW = 5;
            internal const uint WM_CLOSE = 0x0010;
            internal const uint TH32CS_SNAPPROCESS = 0x00000002;
            internal static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

            internal delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            internal struct PROCESSENTRY32
            {
                internal uint dwSize;
                internal uint cntUsage;
                internal uint th32ProcessID;
                internal IntPtr th32DefaultHeapID;
                internal uint th32ModuleID;
                internal uint cntThreads;
                internal uint th32ParentProcessID;
                internal int pcPriClassBase;
                internal uint dwFlags;

                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                internal string szExeFile;
            }

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern int GetWindowLong(IntPtr window, int index);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern int SetWindowLong(IntPtr window, int index, int newValue);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            internal static extern IntPtr GetParent(IntPtr window);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern IntPtr SetWindowPos(
                IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, int flags);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ShowWindow(IntPtr window, int command);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool BringWindowToTop(IntPtr window);

            [DllImport("user32.dll")]
            internal static extern IntPtr SetFocus(IntPtr window);

            [DllImport("user32.dll")]
            internal static extern IntPtr GetFocus();

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachInput);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsWindow(IntPtr window);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool CloseHandle(IntPtr handle);

            [DllImport("kernel32.dll")]
            internal static extern void SetLastError(uint errorCode);
        }
    }
}
