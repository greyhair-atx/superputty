/*
 * Copyright (c) 2009 Jim Radford http://www.jimradford.com
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions: 
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using System.IO;
using log4net;
using System.Configuration;
using System.Collections.Generic;
using SuperPutty.Utils;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace SuperPutty
{
    public delegate void PuttyClosedCallback(bool error);

    public class ApplicationPanel : System.Windows.Forms.Panel
    {
        #region Private Member Variables

        private static readonly ILog Log = LogManager.GetLogger(typeof(ApplicationPanel));

        private static bool RefocusOnVisChanged = Convert.ToBoolean(ConfigurationManager.AppSettings["SuperPuTTY.RefocusOnVisChanged"] ?? "False");
        private static bool LoopWaitForHandle = Convert.ToBoolean(ConfigurationManager.AppSettings["SuperPuTTY.LoopWaitForHandle"] ?? "False");
        private static int ClosePuttyWaitTimeMs = Convert.ToInt32(ConfigurationManager.AppSettings["SuperPuTTY.ClosePuttyWaitTimeMs"] ?? "100");
        private static string ActivatorTypeName = ConfigurationManager.AppSettings["SuperPuTTY.ActivatorTypeName"] ?? typeof(KeyEventWindowActivator).FullName;

        private Process m_Process;
        private CancellationTokenSource startupCancellation;
        internal volatile bool ScriptsStopped;
        private bool startupInProgress;
        private bool m_Created = false;
        private IntPtr m_AppWin;
        private List<IntPtr> m_hWinEventHooks = new List<IntPtr>();
        private List<NativeMethods.WinEventDelegate> lpfnWinEventProcs = new List<NativeMethods.WinEventDelegate>();
        private WindowActivator m_windowActivator = null;
        private SuperPutty.Data.ConnectionProtocol proto;
        private int suppressNextForegroundActivation;
        private System.Windows.Forms.Timer vncWindowTracker;

        internal PuttyClosedCallback m_CloseCallback;
        private event Action Captured;

        internal virtual bool ScriptInputReady { get { return !ScriptsStopped && ExternalProcessCaptured; } }

        internal void WhenCaptured(Action action)
        {
            if (ScriptInputReady)
                action();
            else
                Captured += action;
        }

        protected void NotifyWindowCaptured()
        {
            Action captured = Captured;
            Captured = null;
            captured?.Invoke();
        }


        /// <summary>Set the name of the application executable to launch</summary>
        [Category("Data"), Description("The path/file to launch"), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public string ApplicationName { get; set; }

        [Category("Data"), Description("The parameters to pass to the application being launched"),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public string ApplicationParameters { get; set; }

        [Category("Data"), Description("The starting directory for the putty shell.  Relevant only to cygterm sessions"),
DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public string ApplicationWorkingDirectory { get; set; }

        public virtual IntPtr AppWindowHandle { get { return this.m_AppWin; } }

        /// <summary>
        /// Managed child hosts override this to bypass the external-process window
        /// capture and reparenting lifecycle.
        /// </summary>
        protected virtual bool UsesManagedChildHost { get { return false; } }
        
        // Some windows need closed with WM_DESTROY others need closed with WM_CLOSE or they leave zombies
        public bool ApplicationCloseWithDestroy { get; set; }

        #endregion

        public ApplicationPanel(SuperPutty.Data.ConnectionProtocol proto)
        {
            this.ApplicationName = "";
            this.ApplicationParameters = "";
            this.ApplicationWorkingDirectory = "";
            this.proto = proto;

            this.Disposed += new EventHandler(ApplicationPanel_Disposed);
            SuperPuTTY.LayoutChanged += new EventHandler<Data.LayoutChangedEventArgs>(SuperPuTTY_LayoutChanged);

            // setup up the hook to watch for all EVENT_SYSTEM_FOREGROUND events system wide

            string typeName = string.IsNullOrEmpty(SuperPuTTY.Settings.WindowActivator) ? ActivatorTypeName : SuperPuTTY.Settings.WindowActivator;
            this.m_windowActivator = (WindowActivator)Activator.CreateInstance(Type.GetType(typeName));
            //this.m_windowActivator = new SetFGCombinedWindowActivator();
            SuperPuTTY.Settings.SettingsSaving += Settings_SettingsSaving;
            if (SuperPuTTY.WindowEvents != null)
            {
                SuperPuTTY.WindowEvents.SystemSwitch += new EventHandler<GlobalWindowEventArgs>(OnSystemSwitch);
            }
        }

        void Settings_SettingsSaving(object sender, CancelEventArgs e)
        {
            if (this.UsesManagedChildHost)
            {
                return;
            }

            this.UpdateTitle();
        }

        void ApplicationPanel_Disposed(object sender, EventArgs e)
        {
            ScriptsStopped = true;
            CancelStartup();
            StopVncWindowTracker();
            this.Disposed -= new EventHandler(ApplicationPanel_Disposed);
            SuperPuTTY.LayoutChanged -= new EventHandler<Data.LayoutChangedEventArgs>(SuperPuTTY_LayoutChanged);
            SuperPuTTY.Settings.SettingsSaving -= Settings_SettingsSaving;
            if (SuperPuTTY.WindowEvents != null)
            {
                SuperPuTTY.WindowEvents.SystemSwitch -= new EventHandler<GlobalWindowEventArgs>(OnSystemSwitch);
            }
            this.m_hWinEventHooks.ForEach(delegate(IntPtr hook) {
                NativeMethods.UnhookWinEvent(hook);
            });
            this.m_hWinEventHooks.Clear();
            this.lpfnWinEventProcs.Clear();
            Captured = null;
        }

        void SuperPuTTY_LayoutChanged(object sender, Data.LayoutChangedEventArgs e)
        {
            // move 1x after we're done loading
            this.MoveWindow("LayoutChanged");
        }

        public virtual void RefreshAppWindow()
        {
            this.MoveWindow("RefreshWindow");
        }

        private void MoveWindow(string src)
        {
            // if there is more than one screen and we're maximizing the window on the non-primary screen
            // and the non-primary screen has greater resolution than the primary, do an extra move window
            if (Screen.AllScreens.Length > 1 && SuperPuTTY.MainForm.WindowState == FormWindowState.Maximized)
            {
                Screen screen = Screen.FromControl(this);
                Screen primary = Screen.PrimaryScreen;
                int screenArea = screen.WorkingArea.Height * screen.WorkingArea.Width;
                int primaryArea = primary.WorkingArea.Height * primary.WorkingArea.Width;
                if (screen != primary && screenArea > primaryArea)
                {
                    this.MoveWindow("2ndScreenFix", 0, 1);
                }
            }
            MoveWindow(src, 0, 0);
        }

        private void MoveWindow(string src, int x, int y)
        {
            if (this.UsesManagedChildHost)
            {
                return;
            }

            if (!SuperPuTTY.IsLayoutChanging)
            {
                bool success = NativeMethods.MoveWindow(m_AppWin, x, y, this.Width, this.Height, this.Visible);
                if (Log.IsInfoEnabled)
                {
                    Log.InfoFormat("MoveWindow [{3,-15}{4,20}] w={0,4}, h={1,4}, visible={2}, success={5}, hnd={6}", this.Width, this.Height, this.Visible, src, this.Name, success, m_AppWin);
                }
            }
        }

        public virtual bool ReFocusPuTTY(string caller)
        {
            bool result = false;
            if (this.proto == SuperPutty.Data.ConnectionProtocol.RDP) /* Otherwise window will be hidden and require SuperPutTTY minimize-restore cycle */
                this.MoveWindow("RestoreTabSwitch");
            if (this.ExternalProcessCaptured && NativeMethods.GetForegroundWindow() != this.m_AppWin)
            {
                Interlocked.Exchange(ref suppressNextForegroundActivation, 1);
                result = NativeMethods.SetForegroundWindow(this.m_AppWin);
                if (!result)
                {
                    Interlocked.Exchange(ref suppressNextForegroundActivation, 0);
                }
                if (result)
                    NativeMethods.InvalidateRect(this.m_AppWin, IntPtr.Zero, false);
                Log.InfoFormat("[{0}] ReFocusPuTTY - puttyTab={1}, caller={2}, result={3}", this.m_AppWin, this.Parent.Text, caller, result);
            }

            return result;
        }

        /// <summary>
        /// Wait for child process tree to reach final window state.
        /// Process can spawn children and end itself, so try to track it if needed
        ///  and return new process if needed
        /// </summary>
        /// <param name="prc">The first child process to start tracing on</param>
        internal static Task WaitForInputIdleAsync(Process process, int timeoutMs, CancellationToken cancellation)
        {
            return Task.Run(() =>
            {
                Stopwatch elapsed = Stopwatch.StartNew();
                while (elapsed.ElapsedMilliseconds < timeoutMs)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (process.HasExited || process.WaitForInputIdle(Math.Min(100,
                        Math.Max(1, timeoutMs - (int)elapsed.ElapsedMilliseconds))))
                        return;
                }
                throw new TimeoutException("The application did not become ready before the startup timeout.");
            }, cancellation);
        }

        private void CancelStartup()
        {
            startupCancellation?.Cancel();
            if (!startupInProgress)
                return;
            try
            {
                if (m_Process != null && !m_Process.HasExited)
                    m_Process.Kill();
            }
            catch (InvalidOperationException) { }
            catch (Win32Exception ex) { Log.Warn("Unable to stop the starting application", ex); }
        }

        internal static bool TryGetProcessWindow(Process process, out IntPtr window)
        {
            window = IntPtr.Zero;
            try
            {
                process.Refresh();
                if (process.HasExited)
                    return false;
                window = process.MainWindowHandle;
                return true;
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The viewer can exit between HasExited and MainWindowHandle.
                return false;
            }
        }

        private void LogStartupExit()
        {
            m_AppWin = IntPtr.Zero;
            Log.WarnFormat("{0} viewer '{1}' exited before window capture completed. Exit code: {2}. Check viewer command-line compatibility and connection settings.",
                this.proto, ApplicationName, m_Process.ExitCode);
        }

        internal static void ReparentWindow(IntPtr child, IntPtr parent, bool preserveWindowStyle = false)
        {
            if (!NativeMethods.IsWindow(child) || !NativeMethods.IsWindow(parent))
                throw new InvalidOperationException("The application or host window no longer exists.");
            NativeMethods.ClearLastError(0);
            int originalStyle = NativeMethods.GetWindowLong(child, NativeMethods.GWL_STYLE);
            int error = Marshal.GetLastWin32Error();
            if (originalStyle == 0 && error != 0)
                throw new Win32Exception(error);
            // PuTTY uses top-level activation for its active-terminal state. Keep
            // its established hosting style instead of converting it to WS_CHILD.
            int childStyle = preserveWindowStyle ? originalStyle :
                unchecked((int)(((uint)originalStyle | NativeMethods.WS_CHILD) & ~NativeMethods.WS_POPUP));
            NativeMethods.ClearLastError(0);
            int previousStyle = NativeMethods.SetWindowLong(child, NativeMethods.GWL_STYLE, childStyle);
            error = Marshal.GetLastWin32Error();
            if (previousStyle == 0 && error != 0)
                throw new Win32Exception(error, "Unable to prepare the application window for embedding.");
            try
            {
                NativeMethods.ClearLastError(0);
                IntPtr previousParent = NativeMethods.SetParent(child, parent);
                error = Marshal.GetLastWin32Error();
                if (previousParent == IntPtr.Zero && error != 0)
                    throw new Win32Exception(error, "Unable to embed the application window.");
                if (NativeMethods.GetAncestor(child, 1 /* GA_PARENT, excludes owner */) != parent)
                {
                    NativeMethods.SetParent(child, previousParent);
                    throw new InvalidOperationException("The application window was not embedded in its host.");
                }
            }
            catch
            {
                NativeMethods.SetWindowLong(child, NativeMethods.GWL_STYLE, originalStyle);
                throw;
            }
        }

        internal static bool CaptureTimedOut(Data.ConnectionProtocol protocol, bool windowPresented,
            long elapsedMilliseconds, int timeoutMilliseconds)
        {
            // An external RDP client may be waiting on a person to finish authentication.
            return !(protocol == Data.ConnectionProtocol.RDP && windowPresented) &&
                elapsedMilliseconds >= timeoutMilliseconds;
        }

        private void AttachToWindow()
        {
            if (this.m_AppWin != IntPtr.Zero)
            {
                // Set the application as a child of the parent form
                ReparentWindow(m_AppWin, this.Handle, ctlPuttyPanel.SupportsPuttyRestart(this.proto));

                // Show it! (must be done before we set the windows visibility parameters below
                NativeMethods.ShowWindow(m_AppWin, NativeMethods.WindowShowStyle.Maximize);

                // set window parameters (how it's displayed)
                long lStyle = NativeMethods.GetWindowLong(m_AppWin, NativeMethods.GWL_STYLE);
                lStyle &= ~NativeMethods.WS_BORDER;
                if (this.proto == SuperPutty.Data.ConnectionProtocol.VNC)
                    lStyle |= NativeMethods.WS_HSCROLL | NativeMethods.WS_VSCROLL;
                NativeMethods.SetWindowLong(m_AppWin, NativeMethods.GWL_STYLE, unchecked((int)lStyle));
                // Hooks are process-scoped and remain valid when VNC replaces its window.
                if (this.lpfnWinEventProcs.Count == 0)
                {
                    NativeMethods.WinEventDelegate lpfnWinEventProc = new NativeMethods.WinEventDelegate(WinEventProc);
                    this.lpfnWinEventProcs.Add(lpfnWinEventProc);
                    RegisterWinEventHook(NativeMethods.WinEvents.EVENT_OBJECT_NAMECHANGE, lpfnWinEventProc);
                    RegisterWinEventHook(NativeMethods.WinEvents.EVENT_SYSTEM_FOREGROUND, lpfnWinEventProc);
                }
                NotifyWindowCaptured();
            }
            else
            {
                MessageBox.Show("Process window not found.", "Process Window Not Found");
                try {
                    m_Process.Kill();
                } 
                catch (InvalidOperationException ex)
                {
                    Log.WarnFormat("no process window found to kill: {0}", ex.Message);
                }
                return;
            }
        }

        private int GetMaxWindowPoolingTime()
        {
            return this.proto == SuperPutty.Data.ConnectionProtocol.RDP ? 30 : 10;
        }

        private void StopVncWindowTracker()
        {
            if (vncWindowTracker != null)
            {
                vncWindowTracker.Stop();
                vncWindowTracker.Dispose();
                vncWindowTracker = null;
            }
        }

        internal static bool IsTigerVncDesktopWindow(IntPtr window)
        {
            if (window == IntPtr.Zero || !NativeMethods.IsWindow(window))
                return false;
            StringBuilder title = new StringBuilder(512);
            NativeMethods.GetWindowText(window, title, title.Capacity);
            // TigerVNC's DesktopWindow adds this suffix to the server's desktop name.
            // Authentication, certificate and options dialogs do not use it.
            string caption = title.ToString();
            return caption.EndsWith(" - TigerVNC", StringComparison.Ordinal) ||
                caption.Contains(" - TigerVNC (");
        }

        internal static IntPtr FindTigerVncDesktopWindow(uint processId)
        {
            IntPtr result = IntPtr.Zero;
            NativeMethods.EnumDesktopWindows(IntPtr.Zero, delegate(IntPtr window, int parameter)
            {
                uint owner;
                NativeMethods.GetWindowThreadProcessId(window, out owner);
                if (owner == processId && NativeMethods.IsWindowVisible(window) && IsTigerVncDesktopWindow(window))
                {
                    result = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private void TrackVncWindow(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing || m_Process == null || m_Process.HasExited)
            {
                StopVncWindowTracker();
                return;
            }
            // Keep a captured desktop even when its tab is hidden. Dialogs must
            // never replace it; only look again after the desktop is destroyed.
            if (IsTigerVncDesktopWindow(m_AppWin))
                return;
            IntPtr window = FindTigerVncDesktopWindow((uint)m_Process.Id);
            if (window == IntPtr.Zero)
                return;

            Log.InfoFormat("Replacing VNC startup window {0} with desktop window {1}", m_AppWin, window);
            m_AppWin = window;
            try
            {
                this.AttachToWindow();
                this.MoveWindow("VncDesktopCapture");
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
            {
                m_AppWin = IntPtr.Zero;
                StopVncWindowTracker();
                Log.Warn("Unable to embed the VNC desktop", ex);
                SuperPuTTY.ReportStatus("The VNC desktop could not be embedded. Close and reopen the session to retry.");
            }
        }

        private bool IsWindowAppliesForInherit(IntPtr hWnd)
        {
            switch (this.proto)
            {
                case SuperPutty.Data.ConnectionProtocol.RDP:
                    StringBuilder winTitleBuf = new StringBuilder(256);
                    int winTitleLen = NativeMethods.GetWindowText(hWnd, winTitleBuf, winTitleBuf.Capacity - 1);
                    if (winTitleLen > 0 && (winTitleBuf.ToString().Contains(" - Remote Desktop Connection") || winTitleBuf.ToString().Contains("FreeRDP: ")))
                        return true;
                    return false;
                default:
                    return true;
            }
        }

        #region Focus Change Handling
        /*************************** Begin Hack to watch for windows focus change events **************************************
        * This is based on this form post:
        * http://social.msdn.microsoft.com/Forums/en-US/clr/thread/c04e343f-f2e7-469a-8a54-48ca84f78c28
        *
        * The idea is to watch for the EVENT_SYSTEM_FOREGROUND window, and when we see that from the putty terminal window
        * bring the superputty window to the foreground
         * 
         * Other hacks:
         * http://stackoverflow.com/questions/4867210/how-to-bring-a-window-foreground-using-c
         * http://stackoverflow.com/questions/46030/c-sharp-force-form-focus
        */

        bool isSwitchingViaAltTab = false;

        void OnSystemSwitch(object sender, GlobalWindowEventArgs e)
        {
            switch (e.eventType)
            {
                case (uint)NativeMethods.WinEvents.EVENT_SYSTEM_SWITCHSTART:
                    this.isSwitchingViaAltTab = true;
                    break;
                case (uint)NativeMethods.WinEvents.EVENT_SYSTEM_SWITCHEND:
                    this.isSwitchingViaAltTab = false;
                    break;
            }
        }

        void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (m_AppWin != hwnd)
                return;

            switch (eventType)
            {
                case (uint)NativeMethods.WinEvents.EVENT_OBJECT_NAMECHANGE:
                    RunOnUiThread(UpdateTitle);
                    break;
                case (uint)NativeMethods.WinEvents.EVENT_SYSTEM_FOREGROUND:
                    if (Interlocked.Exchange(ref suppressNextForegroundActivation, 0) == 0)
                    {
                        RunOnUiThread(UpdateForeground);
                    }
                    break;
            }
        }

        void UpdateForeground()
        {
            // if we got the EVENT_SYSTEM_FOREGROUND, and the hwnd is the putty terminal hwnd (m_AppWin)
            // then bring the SuperPuTTY window to the foreground
            if (m_AppWin != NativeMethods.GetForegroundWindow())
            {
                return;
            }

            Log.DebugFormat("[{0}] HandlingForegroundEvent", m_AppWin);

            // This is the easiest way I found to get the superputty window to be brought to the top
            // if you leave TopMost = true; then the window will always be on top.
            if (this.TopLevelControl != null)
            {
                Form form = SuperPuTTY.MainForm;
                if (form.WindowState == FormWindowState.Minimized)
                {
                    return;
                }

                DesktopWindow window = DesktopWindow.GetFirstDesktopWindow();
                this.m_windowActivator.ActivateForm(form, window, m_AppWin);

                // focus back to putty via setting active dock panel
                ctlPuttyPanel parent = (ctlPuttyPanel)this.Parent;
                if (parent != null && parent.DockPanel != null)
                {
                    if (parent.DockPanel.ActiveDocument != parent && parent.DockState == DockState.Document)
                    {
                        string activeDoc = parent.DockPanel.ActiveDocument != null
                            ? ((ToolWindow)parent.DockPanel.ActiveDocument).Text : "?";
                        Log.InfoFormat("[{0}] Setting Active Document: {1} -> {2}", m_AppWin, activeDoc, parent.Text);
                        parent.Show();
                    }
                    else
                    {
                        // give focus back
                        this.ReFocusPuTTY("WinEventProc-FG, AltTab=" + isSwitchingViaAltTab);
                    }
                }
            }
        }

        private void RegisterWinEventHook(NativeMethods.WinEvents eventType, NativeMethods.WinEventDelegate callback)
        {
            uint eventId = (uint)eventType;
            IntPtr hook = NativeMethods.SetWinEventHook(eventId, eventId, IntPtr.Zero, callback,
                (uint)m_Process.Id, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            if (hook == IntPtr.Zero)
            {
                Log.WarnFormat("Unable to register {0} for process {1}. Win32Error={2}",
                    eventType, m_Process.Id, System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            }
            else
            {
                this.m_hWinEventHooks.Add(hook);
            }
        }

        private void RunOnUiThread(Action action)
        {
            if (action == null || IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (!IsDisposed && !Disposing)
                        {
                            action();
                        }
                    }));
                }
                else
                {
                    action();
                }
            }
            catch (InvalidOperationException)
            {
                // The panel was disposed after the WinEvent callback was delivered.
            }
        }

        private void UpdateTitle()
        {
            string controlText;
            if (!TryReadWindowTitle(m_AppWin, out controlText) || !(this.Parent is ctlPuttyPanel))
                return;
            string parentText = ((ctlPuttyPanel)this.Parent).TextOverride;

            switch ((SuperPutty.frmSuperPutty.TabTextBehavior)Enum.Parse(typeof(frmSuperPutty.TabTextBehavior), SuperPuTTY.Settings.TabTextBehavior))
            {
                case frmSuperPutty.TabTextBehavior.Static:
                    this.Parent.Text = parentText;
                    break;
                case frmSuperPutty.TabTextBehavior.Dynamic:
                    this.Parent.Text = controlText;
                    break;
                case frmSuperPutty.TabTextBehavior.Mixed:
                    this.Parent.Text = parentText + ": " + controlText;
                    break;
            }
        }

        public event EventHandler InnerApplicationFocused;

        void OnInnerApplicationFocused()
        {
            if (this.InnerApplicationFocused != null)
            {
                this.InnerApplicationFocused(this, EventArgs.Empty);
            }
        }

        /*************************** End Hack to watch for windows focus change events ***************************************/
        #endregion

        #region Base Overrides
       
        /// <summary>
        /// Force redraw of control when size changes
        /// </summary>
        /// <param name="e">Not used</param>
        protected override void OnSizeChanged(EventArgs e)
        {
            this.Invalidate();
            base.OnSizeChanged(e);
        }
       
        /// <summary>
        /// Create (start) the hosted application when the parent becomes visible
        /// </summary>
        /// <param name="e">Not used</param>
        protected override async void OnVisibleChanged(EventArgs e)
        {
            if (this.UsesManagedChildHost)
            {
                base.OnVisibleChanged(e);
                return;
            }

            if (Visible && !m_Created && !String.IsNullOrEmpty(ApplicationName)) // only allow one instance of the child
            {
                m_Created = true;
                startupInProgress = true;
                startupCancellation = new CancellationTokenSource();
                CancellationToken cancellation = startupCancellation.Token;
                m_AppWin = IntPtr.Zero;
                try
                {
                    if (!File.Exists(ApplicationName))
                    {
                        MessageBox.Show(ApplicationName + " not found in configured path, please go into tools->settings and set the correct path", "Application Not Found");
                        return;
                    }
                    m_Process = new Process
                    {
                        EnableRaisingEvents = true,
                        StartInfo =
                        {
                            FileName = ApplicationName,
                            Arguments = ApplicationParameters,
                            WindowStyle = ProcessWindowStyle.Maximized
                        }
                    };

                    if (!string.IsNullOrEmpty(this.ApplicationWorkingDirectory) &&
                        Directory.Exists(this.ApplicationWorkingDirectory))
                    {
                        m_Process.StartInfo.WorkingDirectory = this.ApplicationWorkingDirectory;
                    }

                    m_Process.Exited += delegate {
                        RunOnUiThread(() => m_CloseCallback?.Invoke(true));
                    };

                    m_Process.Start();

                    await WaitForInputIdleAsync(m_Process, GetMaxWindowPoolingTime() * 1000, cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    if (IsDisposed || Disposing || !IsHandleCreated)
                        return;
                    Stopwatch captureWait = Stopwatch.StartNew();
                    bool windowPresented = false;
                    while (true)
                    {
                        IntPtr window;
                        if (!TryGetProcessWindow(m_Process, out window))
                        {
                            LogStartupExit();
                            return;
                        }
                        if (window != IntPtr.Zero && NativeMethods.IsWindow(window))
                            windowPresented = true;
                        if (window != IntPtr.Zero && IsWindowAppliesForInherit(window))
                        {
                            m_AppWin = window;
                            break;
                        }
                        if ((!LoopWaitForHandle && proto != Data.ConnectionProtocol.RDP) ||
                            CaptureTimedOut(proto, windowPresented, captureWait.ElapsedMilliseconds, GetMaxWindowPoolingTime() * 1000))
                            throw new TimeoutException("No application window appeared before the capture timeout.");
                        await Task.Delay(50, cancellation);
                        if (IsDisposed || Disposing || !IsHandleCreated)
                            return;
                    }

                    if (m_Process.HasExited)
                    {
                        LogStartupExit();
                        return;
                    }
                    string title = m_Process.MainWindowTitle;
                    if (SuperPuTTY.PuTTYAppName + " Command Line Error" == title)
                    {
                        Log.WarnFormat("Error while creating putty session: title={0}, handle={1}. Abort capture window", title, this.m_AppWin);
                        MessageBox.Show("Could not start putty session: Arguments passed to commandline invalid.", "putty command line error.");
                        this.m_AppWin = IntPtr.Zero;
                    }

                    this.AttachToWindow();
                    if (this.proto == SuperPutty.Data.ConnectionProtocol.VNC && VNCStartInfo.IsTigerVncExecutable(ApplicationName))
                    {
                        vncWindowTracker = new System.Windows.Forms.Timer { Interval = 200 };
                        vncWindowTracker.Tick += TrackVncWindow;
                        vncWindowTracker.Start();
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception || ex is TimeoutException)
                {
                    bool wasCanceled = cancellation.IsCancellationRequested;
                    CancelStartup();
                    m_AppWin = IntPtr.Zero;
                    Log.Warn("Unable to start hosted application", ex);
                    if (!IsDisposed && !Disposing && !wasCanceled)
                        MessageBox.Show(this, "The application could not be started or captured. Check its path and connection settings.",
                            "Application Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    startupInProgress = false;
                    startupCancellation.Dispose();
                    startupCancellation = null;
                }

            }

            if (IsDisposed || Disposing || !IsHandleCreated)
                return;
            if (this.Visible && this.m_Created && this.ExternalProcessCaptured)
            {
                // Move the child so it's located over the parent
                this.MoveWindow("OnVisChanged");
                
                if (RefocusOnVisChanged && NativeMethods.GetForegroundWindow() != this.m_AppWin)
                {
                    this.BeginInvoke(new MethodInvoker(delegate { this.ReFocusPuTTY("OnVisChanged"); }));
                }
            }

            base.OnVisibleChanged(e);
        }

        
        /// <summary>
        /// Send a close message to the hosted application window when the parent is destroyed
        /// </summary>
        /// <param name="e"></param>
        protected override void OnHandleDestroyed(EventArgs e)
        {
            ScriptsStopped = true;
            CancelStartup();
            StopVncWindowTracker();
            if (this.UsesManagedChildHost)
            {
                base.OnHandleDestroyed(e);
                return;
            }

            if (this.ExternalProcessCaptured)
            {
                // Send WM_DESTROY instead of WM_CLOSE, so that the Client doesn't
                // ask in the Background whether the session shall be closed.
                // Otherwise an annoying beep is generated everytime a terminal session is closed.
                if (this.ApplicationCloseWithDestroy) {
                    NativeMethods.PostMessage(m_AppWin, NativeMethods.WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
                } else {
                    NativeMethods.PostMessage(m_AppWin, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }

                System.Threading.Thread.Sleep(ClosePuttyWaitTimeMs);

                m_AppWin = IntPtr.Zero;
            }

            base.OnHandleDestroyed(e);
        }

        /// <summary>
        /// Refresh the hosted applications window when the parent changes size
        /// </summary>
        /// <param name="e"></param>
        protected override void OnResize(EventArgs e)
        {
            if (this.UsesManagedChildHost)
            {
                base.OnResize(e);
                return;
            }

            // if valid
            if (ExternalProcessCaptured)
            {
                // if not minimizing && visible
                if (this.Height > 0 && this.Width > 0 && this.Visible)
                {
                    this.MoveWindow("OnResize");
                }
            }
            base.OnResize(e);
        }

        public virtual bool ExternalProcessCaptured { get { return this.m_AppWin != IntPtr.Zero; } }

        public virtual bool IsSessionActive
        {
            get
            {
                if (UsesManagedChildHost)
                    return ExternalProcessCaptured;
                try { return m_Process != null && !m_Process.HasExited; }
                catch (InvalidOperationException) { return false; }
            }
        }

        internal static bool TryReadWindowTitle(IntPtr window, out string title)
        {
            title = null;
            if (window == IntPtr.Zero || !NativeMethods.IsWindow(window))
                return false;
            // Bound both calls, including when the target thread keeps pumping messages.
            const uint flags = 0x0002 | 0x0020; // SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT
            UIntPtr result;
            if (NativeMethods.SendMessageTimeout(window, NativeMethods.WM_GETTEXTLENGTH,
                IntPtr.Zero, IntPtr.Zero, flags, 100, out result) == IntPtr.Zero)
                return false;
            int capacity = (int)Math.Min(result.ToUInt64(), 32767UL) + 1;
            StringBuilder text = new StringBuilder(capacity);
            if (NativeMethods.SendMessageTimeout(window, NativeMethods.WM_GETTEXT,
                new IntPtr(capacity), text, flags, 100, out result) == IntPtr.Zero)
                return false;
            title = text.ToString();
            return true;
        }

        #endregion    
    
    }

}
