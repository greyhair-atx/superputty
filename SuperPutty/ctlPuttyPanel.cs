/*
 * Copyright (c) 2009 - 2015 Jim Radford http://www.jimradford.com
 * Copyright (c) 2012 John Peterson
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
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using log4net;
using System.Diagnostics;
using System.Web;
using System.Collections.Specialized;
using SuperPutty.Data;
using WeifenLuo.WinFormsUI.Docking;
using SuperPutty.Utils;
using System.Threading;
using System.Threading.Tasks;
using System.Configuration;
using SuperPutty.Gui;
using log4net.Core;


namespace SuperPutty
{
    /// <summary>A control that hosts a putty window</summary>
    public partial class ctlPuttyPanel : ToolWindowDocument
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ctlPuttyPanel));

        private static int RefocusAttempts = Convert.ToInt32(ConfigurationManager.AppSettings["SuperPuTTY.RefocusAttempts"] ?? "3");
        private static int RefocusIntervalMs = Convert.ToInt32(ConfigurationManager.AppSettings["SuperPuTTY.RefocusIntervalMs"] ?? "40");
        private bool closeWithoutConfirmation;
        private readonly ChildFocusRetry focusRetry;
        private readonly PuttyClosedCallback applicationExit;
        private readonly System.Windows.Forms.Timer startupTimer;
        private readonly ToolStripMenuItem reconnectMenuItem;
        private TaskCompletionSource<string> startup;
        private FlowLayoutPanel startupError;
        private bool reconnecting;
        internal Task<string> Startup { get { return startup.Task; } }
        internal bool ReportOpened { get; set; }

        public ctlPuttyPanel(SessionData session, PuttyClosedCallback callback)
        {
            Session = session.CreateSnapshot();
            AssociateSavedSession(session);
            applicationExit = callback;
            InitializeComponent();
            reconnectMenuItem = new ToolStripMenuItem("Reconnect with Saved Settings", null, ReconnectWithSavedSettings);
            contextMenuStrip1.Items.Insert(contextMenuStrip1.Items.IndexOf(restartSessionToolStripMenuItem) + 1, reconnectMenuItem);
            focusRetry = new ChildFocusRetry();
            components.Add(focusRetry);
            startupTimer = new System.Windows.Forms.Timer(components) { Interval = 120000 };
            startupTimer.Tick += (sender, e) =>
            {
                if (AppPanel?.IsWaitingForStartupInput != true) FailStartup("Session startup timed out.");
            };
            Disposed += (sender, e) => startup.TrySetResult("The tab closed during startup.");

            this.Text = session.SessionName;
            this.TabText = session.SessionName;
            this.TextOverride = session.SessionName;

            StartClient();
        }

        /// <summary>Gets or sets the text displayed on a tab.</summary>
        public override string Text
        {
            get
            {
                return base.Text;
            }
            set
            {
                this.TabText = value != null ? value.Replace("&", "&&") : null;
                base.Text = value;

                if (Log.Logger.IsEnabledFor(Level.Trace))
                {
                    Log.DebugFormat("SetText: text={0}", value);
                }
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            this.ToolTipText = this.Text;
        }

        internal void WatchStartup()
        {
            if (!Startup.IsCompleted && !IsDisposed && !Disposing)
            {
                if (AppPanel?.IsWaitingForStartupInput != true) startupTimer.Start();
                AppPanel?.EnsureStarted();
            }
        }

        private void StartClient()
        {
            startup = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            AppPanel = null;
            try
            {
                CreatePanel();
                AdjustMenu();
            }
            catch (Exception ex) when (SuperPuTTY.IsSessionLaunchError(ex))
            {
                Log.Warn("Could not configure the session client.", ex);
                FailStartup("The client could not be configured. Check the connection and client settings.");
            }
        }

        private void CreatePanel()
        {
            string executable = PuttyStartInfo.GetExecutable(Session);
            if (RdpClientPanel.ShouldUseActiveX(Session, executable))
            {
                RdpClientPanel rdpPanel;
                if (RdpClientPanel.TryCreate(Session, null, out rdpPanel))
                    AppPanel = rdpPanel;
                else
                    Log.Warn("Falling back to external MSTSC hosting because the RDP ActiveX control is unavailable.");
            }
            if (this.AppPanel == null)
            {
                var startInfo = new PuttyStartInfo(Session);
                this.AppPanel = ConsoleApplicationPanel.Supports(this.Session.Proto)
                    ? new ConsoleApplicationPanel(this.Session.Proto)
                    : new ApplicationPanel(this.Session.Proto);
                this.AppPanel.ApplicationName = startInfo.Executable;
                this.AppPanel.ApplicationParameters = startInfo.Args;
                this.AppPanel.ApplicationWorkingDirectory = startInfo.WorkingDir;
                this.AppPanel.ApplicationCloseWithDestroy = this.Session.Proto != ConnectionProtocol.Mintty;
            }

            ApplicationPanel client = AppPanel;
            client.Dock = DockStyle.Fill;
            client.Name = Session.SessionId;
            client.Size = Size;
            client.StartupFailed = message => PostClientCallback(client, () => ShowClientFailure(message));
            client.StartupInputChanged = waiting =>
            {
                if (!ReferenceEquals(client, AppPanel) || IsDisposed || Disposing || Startup.IsCompleted) return;
                if (waiting)
                {
                    startupTimer.Stop();
                    SuperPuTTY.ReportStatus("{0} is waiting for RDP sign-in.", Session.SessionId);
                }
                else startupTimer.Start();
            };
            client.m_CloseCallback = error => PostClientCallback(client, () =>
            {
                if (Startup.IsCompleted)
                {
                    if (startupError == null)
                    {
                        if (applicationExit != null) applicationExit(error);
                        else CloseWithoutConfirmation();
                    }
                }
                else FailStartup("The client disconnected before session startup completed.");
            });
            client.WhenCaptured(() => PostClientCallback(client, CompleteStartup));
            Controls.Add(client);
        }

        private void PostClientCallback(ApplicationPanel client, Action callback)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed && !Disposing && ReferenceEquals(client, AppPanel) && !client.IsDisposed)
                        callback();
                }));
            }
            catch (InvalidOperationException) { }
        }

        private void CompleteStartup()
        {
            if (!startup.TrySetResult(null)) return;
            startupTimer.Stop();
            if (ReportOpened) SuperPuTTY.ReportStatus("Opened session {0} [{1}]", Session.SessionId, Session.Proto);
            if (!String.IsNullOrWhiteSpace(Session.SPSLFileName))
                SuperPuTTY.StartSessionScript(this, Session.SPSLFileName);
        }

        private void FailStartup(string message)
        {
            if (!Startup.IsCompleted) ShowClientFailure(message);
        }

        private void ShowClientFailure(string message)
        {
            if (startupError != null) return;
            startup.TrySetResult(message);
            startupTimer.Stop();
            focusRetry.Stop();
            AppPanel?.Dispose();
            startupError = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            var label = new Label { AutoSize = true, Text = message };
            var retry = new Button { AutoSize = true, Text = "Retry" };
            var close = new Button { AutoSize = true, Text = "Close" };
            startupError.Controls.Add(label);
            startupError.SetFlowBreak(label, true);
            startupError.Controls.Add(retry);
            startupError.Controls.Add(close);
            retry.Click += (sender, e) => RetryStartup();
            close.Click += (sender, e) => CloseWithoutConfirmation();
            Controls.Add(startupError);
            SuperPutty.Gui.ThemeManager.Apply(startupError);
        }

        internal void RetryStartup()
        {
            if (startupError == null || IsDisposed || Disposing) return;
            startupError.Dispose();
            startupError = null;
            StartClient();
            WatchStartup();
        }

        private async void ReconnectWithSavedSettings(object sender, EventArgs e)
        {
            SessionData saved = GetCurrentSavedSession();
            if (saved == null || SuperPuTTY.IsScpSession(saved) || !Startup.IsCompleted || reconnecting) return;
            if (AppPanel != null && AppPanel.ConnectionState != SessionConnectionState.Disconnected &&
                MessageBox.Show(this, $"Reconnect '{TextOverride ?? Text}' with its saved settings?",
                    "Reconnect Session", MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK)
                return;
            saved = GetCurrentSavedSession();
            if (IsDisposed || Disposing || saved == null || SuperPuTTY.IsScpSession(saved) || reconnecting) return;

            reconnecting = true;
            focusRetry.Stop();
            ApplicationPanel previousClient = AppPanel;
            previousClient?.Dispose();
            startupError?.Dispose();
            startupError = null;
            Session = saved.CreateSnapshot();
            reconnectMenuItem.Enabled = false;
            if (previousClient != null) await previousClient.ProcessCleanup;
            reconnecting = false;
            if (IsDisposed || Disposing || !ReferenceEquals(previousClient, AppPanel)) return;
            StartClient();
            WatchStartup();
        }

        void AdjustMenu()
        {
            bool puttyCommands = SupportsPuttyRestart(Session.Proto);
            toolStripPuttySep1.Visible = puttyCommands;
            eventLogToolStripMenuItem.Visible = puttyCommands;
            toolStripPuttySep2.Visible = puttyCommands;
            changeSettingsToolStripMenuItem.Visible = puttyCommands;
            copyAllToClipboardToolStripMenuItem.Visible = puttyCommands;
            restartSessionToolStripMenuItem.Visible = puttyCommands;
            clearScrollbackToolStripMenuItem.Visible = puttyCommands;
            resetTerminalToolStripMenuItem.Visible = puttyCommands;
            resetAndClearScrollbackToolStripMenuItem.Visible = puttyCommands;
        }

        private void InitNewSessionToolStripMenuItems()
        {
            if (IsDisposed || Disposing) return;
            // Rebuild controls on the UI thread and release the previous menu tree.
            foreach (ToolStripItem item in newSessionToolStripMenuItem.DropDownItems.Cast<ToolStripItem>().ToArray())
                item.Dispose();
            foreach (SessionData session in SuperPuTTY.GetAllSessions())
            {
                ToolStripItemCollection siblings = newSessionToolStripMenuItem.DropDownItems;
                string[] parts = SessionData.GetSessionNameParts(session.SessionId);
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    string part = parts[i];
                    ToolStripMenuItem folder = siblings[part] as ToolStripMenuItem;
                    if (folder == null)
                    {
                        folder = new ToolStripMenuItem(part) { Name = part };
                        siblings.Add(folder);
                    }
                    siblings = folder.DropDownItems;
                }
                var item = new ToolStripMenuItem
                {
                    Name = parts[parts.Length - 1], Text = session.SessionName,
                    Tag = session, ToolTipText = session.ToString()
                };
                item.Click += newSessionTSMI_Click;
                siblings.Add(item);
            }
        }

        void CreateMenu()
        {
            SessionData saved = GetCurrentSavedSession();
            reconnectMenuItem.Enabled = saved != null && !SuperPuTTY.IsScpSession(saved) && Startup.IsCompleted && !reconnecting;
            this.newSessionToolStripMenuItem.Enabled = SuperPuTTY.Settings.PuttyPanelShowNewSessionMenu;
            if (SuperPuTTY.Settings.PuttyPanelShowNewSessionMenu)
            {
                InitNewSessionToolStripMenuItems();
            }

            DockPane pane = GetDockPane();
            if (pane != null)
            {
                this.closeOthersToTheRightToolStripMenuItem.Enabled =
                    pane.Contents.IndexOf(this) != pane.Contents.Count - 1;
            }
            this.closeOthersToolStripMenuItem.Enabled = this.DockPanel.DocumentsCount > 1;
            this.closeAllToolStripMenuItem.Enabled = this.DockPanel.DocumentsCount > 1;
        }

        private void closeSessionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Identify the clicked tab before external FormClosing handlers run.
            bool middleClick = e.CloseReason == CloseReason.UserClosing &&
                SuperPutty.Gui.Docking.TabCloseMouseFilter.IsUnconfirmedMiddleClick(this);
            base.OnFormClosing(e);
            if (!e.Cancel && !closeWithoutConfirmation &&
                !middleClick &&
                e.CloseReason == CloseReason.UserClosing &&
                !(SuperPuTTY.MainForm != null && SuperPuTTY.MainForm.IsShuttingDown) &&
                AppPanel != null)
            {
                if (AppPanel.ConnectionState != SessionConnectionState.Disconnected)
                    e.Cancel = !ConfirmSessionClose();
            }
        }

        protected virtual bool ConfirmSessionClose()
        {
            return MessageBox.Show(this, GetCloseConfirmationMessage(TextOverride ?? Text),
                "Confirm Close Session", MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.OK;
        }

        internal static string GetCloseConfirmationMessage(string sessionName)
        {
            return String.Format("Close tab '{0}'?", sessionName);
        }

        internal void CloseWithoutConfirmation()
        {
            closeWithoutConfirmation = true;
            try { Close(); }
            finally { closeWithoutConfirmation = false; }
        }

        internal bool RestartSession()
        {
            if (!SupportsPuttyRestart(Session.Proto) || AppPanel?.ExternalProcessCaptured != true)
                return false;
            puTTYMenuTSMI_Click(restartSessionToolStripMenuItem, EventArgs.Empty);
            return true;
        }

        internal static bool SupportsPuttyRestart(ConnectionProtocol protocol)
        {
            return protocol != ConnectionProtocol.RDP && protocol != ConnectionProtocol.VNC &&
                protocol != ConnectionProtocol.SCP &&
                protocol != ConnectionProtocol.Mintty && !ConsoleApplicationPanel.Supports(protocol);
        }

        private void closeOthersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var docs = from doc in this.DockPanel.DocumentsToArray()
                       where doc is ToolWindowDocument && doc != this
                       select doc as ToolWindowDocument;
            CloseDocs("Close Others", docs);
        }

        private void closeAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var docs = from doc in this.DockPanel.DocumentsToArray()
                       where doc is ToolWindowDocument
                       select doc as ToolWindowDocument;
            CloseDocs("Close All", docs);
        }

        private void closeOthersToTheRightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // find the dock pane with this window
            DockPane pane = GetDockPane();
            if (pane != null)
            {
                // found the pane
                List<ToolWindowDocument> docsToClose = new List<ToolWindowDocument>();
                bool close = false;
                foreach (IDockContent content in new List<IDockContent>(pane.Contents))
                {
                    if (content == this)
                    {
                        close = true;
                        continue;
                    }
                    if (close)
                    {
                        ToolWindowDocument win = content as ToolWindowDocument;
                        if (win != null)
                        {
                            docsToClose.Add(win);
                        }
                    }
                }
                if (docsToClose.Count > 0)
                {
                    CloseDocs("Close Other To the Right", docsToClose);
                }
            }
        }

        void CloseDocs(string source, IEnumerable<ToolWindowDocument> docsToClose)
        {
            int n = docsToClose.Count();
            Log.InfoFormat("Closing mulitple docs: source={0}, count={1}, conf={2}", source, n, SuperPuTTY.Settings.MultipleTabCloseConfirmation);

            bool okToClose = true;
            if (SuperPuTTY.Settings.MultipleTabCloseConfirmation && n > 1)
            {
                okToClose = DialogResult.Yes == MessageBox.Show(this, string.Format("Close {0} Tabs?", n), source, MessageBoxButtons.YesNo);
            }

            if (okToClose)
            {
                foreach (ToolWindowDocument doc in docsToClose)
                {
                    ctlPuttyPanel sessionPanel = doc as ctlPuttyPanel;
                    if (sessionPanel != null && SuperPuTTY.Settings.MultipleTabCloseConfirmation && n > 1)
                        sessionPanel.CloseWithoutConfirmation();
                    else
                        doc.Close();
                }
            }
        }

        DockPane GetDockPane()
        {
            return this.DockPanel.Panes.FirstOrDefault(pane => pane.Contents.Contains(this));
        }

        /// <summary>
        /// Reset the focus to the child application window
        /// </summary>
        internal void SetFocusToChildApplication(string caller)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(() => SetFocusToChildApplication(caller))); }
                catch (InvalidOperationException) { }
                return;
            }
            focusRetry.Request(
                () => !IsDisposed && !Disposing && Visible && AppPanel?.ExternalProcessCaptured == true &&
                    (DockPanel == null || DockPanel.ActiveContent == this) &&
                    (SuperPuTTY.MainForm == null || SuperPuTTY.MainForm.IsForegroundWindow(true)),
                () => NativeMethods.GetForegroundWindow() == AppPanel.AppWindowHandle || AppPanel.ReFocusPuTTY(caller),
                () => Log.WarnFormat("Unable to SetFocusToChildApplication, {0}", Text),
                RefocusAttempts, RefocusIntervalMs);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            focusRetry?.Stop();
            base.OnDeactivate(e);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            focusRetry?.Stop();
            base.OnHandleDestroyed(e);
        }

        protected override string GetPersistString()
        {
            string str = String.Format("{0}?SessionId={1}&TabName={2}", 
                this.GetType().FullName, 
                HttpUtility.UrlEncode((GetCurrentSavedSession() ?? Session).SessionId),
                HttpUtility.UrlEncode(this.TextOverride));
            return str;
        }

        /// <summary>Restore sessions from a string containing previous sessions</summary>
        /// <param name="persistString">A string containing the sessions to restore</param>
        /// <returns>The <seealso cref="ctlPuttyPanel"/> object which is the parent of the hosted putty application, null if unable to start session</returns>
        public static ctlPuttyPanel FromPersistString(String persistString)
        {
            ctlPuttyPanel panel = null;
            if (persistString.StartsWith(typeof(ctlPuttyPanel).FullName))
            {
                int idx = persistString.IndexOf("?");
                if (idx != -1)
                {
                    NameValueCollection data = HttpUtility.ParseQueryString(persistString.Substring(idx + 1));
                    string sessionId = data["SessionId"] ?? data["SessionName"];
                    string tabName = data["TabName"];

                    Log.InfoFormat("Restoring putty session, sessionId={0}, tabName={1}", sessionId, tabName);

                    SessionData session = SuperPuTTY.GetSessionById(sessionId);
                    if (session != null)
                    {
                        panel = SuperPuTTY.OpenProtoSession(session);
                        if (panel == null)
                        {
                            Log.WarnFormat("Could not restore putty session, sessionId={0}", sessionId);
                        }
                        else
                        {
                            panel.Icon = SuperPuTTY.GetIconForSession(session);
                            panel.Text = tabName;
                            panel.TextOverride = tabName;
                        }
                    }
                    else
                    {
                        Log.WarnFormat("Session not found, sessionId={0}", sessionId);
                    }
                }
                else
                {
                    idx = persistString.IndexOf(":");
                    if (idx != -1)
                    {
                        string sessionId = persistString.Substring(idx + 1);
                        Log.InfoFormat("Restoring putty session, sessionId={0}", sessionId);
                        SessionData session = SuperPuTTY.GetSessionById(sessionId);
                        if (session != null)
                        {
                            panel = SuperPuTTY.OpenProtoSession(session);
                        }
                        else
                        {
                            Log.WarnFormat("Session not found, sessionId={0}", sessionId);
                        }
                    }
                }
            }
            return panel;
        }

        private void aboutPuttyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("http://www.chiark.greenend.org.uk/~sgtatham/putty/");
        }

 
        private void duplicateSessionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SuperPuTTY.OpenProtoSession(GetCurrentSavedSession() ?? Session);
        }

        private void renameTabToolStripMenuItem_Click(object sender, EventArgs e)
        {
            dlgRenameItem dialog = new dlgRenameItem
            {
                ItemName = this.Text,
                DetailName = this.Session.SessionId
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                this.Text = dialog.ItemName;
                this.TextOverride = dialog.ItemName;
            }
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.AppPanel != null)
            {
                this.AppPanel.RefreshAppWindow();
            }
        }

        public SessionData Session { get; private set; }
        public ApplicationPanel AppPanel { get; private set; }
        public ctlPuttyPanel previousPanel { get; set; }
        public ctlPuttyPanel nextPanel { get; set; }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            CreateMenu();
        }

        private void newSessionTSMI_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem menuItem = (ToolStripMenuItem) sender;
            SessionData session = menuItem.Tag as SessionData;
            if (session != null)
            {
                SuperPuTTY.OpenProtoSession(session);
            }
        }

        private void puTTYMenuTSMI_Click(object sender, EventArgs e)
        {
            ApplicationPanel client = AppPanel;
            if (client?.ExternalProcessCaptured != true) return;
            ToolStripMenuItem menuItem = (ToolStripMenuItem) sender;
            string[] tags = ((ToolStripMenuItem)sender).Tag.ToString().Split(';');
            uint[] commands = new uint[tags.Length];
            for (int i = 0; i < tags.Length; ++i)
            {
                commands[i] = Convert.ToUInt32(tags[i], 16);
                Log.DebugFormat("Sending Putty Command: menu={2}, tag={0}, command={1}", tags[i], commands[i], menuItem.Text);
            }

            this.SetFocusToChildApplication("MenuHandler");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    for (int i = 0; i < commands.Length; ++i)
                    {
                        IntPtr handle = client.AppWindowHandle;
                        if (handle == IntPtr.Zero) break;
                        NativeMethods.SendMessage(handle, (uint)NativeMethods.WM.SYSCOMMAND, commands[i], 0);
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorFormat("Error sending command menu command to embedded putty", ex);
                }
            });
        }

        public bool AcceptCommands
        {
            get { return this.acceptCommandsToolStripMenuItem.Checked;  }
            set { this.acceptCommandsToolStripMenuItem.Checked = value; }
        }

        public string TextOverride { get; set; }

    }
}
