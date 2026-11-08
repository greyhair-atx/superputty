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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using log4net;
using SuperPutty.Data;
using SuperPutty.Utils;
using WeifenLuo.WinFormsUI.Docking;
using SuperPutty.Gui;
using System.Text.RegularExpressions;


namespace SuperPutty
{
    public delegate void SelectionChangedHandler(SessionData Session);

    public partial class SessionTreeview : ToolWindow, IComparer
    {
        public event SelectionChangedHandler SelectionChanged;
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionTreeview));

        // Legacy configuration name: this prompts before a large Connect All
        // operation; it never caps the number of sessions or open tabs.
        private static readonly int ConnectAllConfirmationThreshold = Convert.ToInt32(ConfigurationManager.AppSettings["SuperPuTTY.MaxSessionsToOpen"] ?? "10");

        public const string SessionIdDelim = "/";
        public const string ImageKeySession = "computer";
        public const string ImageKeyFolder = "folder";

        private DockPanel m_DockPanel;
        private bool isRenamingNode;
        private bool rebuildingTree;
        private bool sessionSavePending;
        private string sessionSaveError;
        TreeNode nodeRoot;
        ImageList imgIcons = new ImageList();
        Func<SessionData, bool> filter;
        private readonly HashSet<SessionData> subscribedSessions = new HashSet<SessionData>();

        public SessionData SelectedSession
        {
            get
            {
                if (treeView1.SelectedNode != null && treeView1.SelectedNode.Tag is SessionData)
                {
                    return treeView1.SelectedNode.Tag as SessionData;
                }
                return null;
            }
        }

        /// <summary>
        /// Instantiate the treeview containing the sessions
        /// </summary>
        /// <param name="dockPanel">The DockPanel container</param>
        /// <remarks>Having the dockpanel container is necessary to allow us to
        /// dock any terminal or file transfer sessions from within the treeview class</remarks>
        public SessionTreeview(DockPanel dockPanel)
        {
            m_DockPanel = dockPanel;
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            InitializeComponent();
            this.treeView1.TreeViewNodeSorter = this;
            this.treeView1.HideSelection = false;
            if (SuperPuTTY.Images != null)
            {
                this.treeView1.ImageList = SuperPuTTY.Images;
            }
            this.ApplySettings();

            // populate sessions in the treeview from the registry
            this.LoadSessions();
            this.ExpandInitialTree();
            SuperPuTTY.Sessions.ListChanged += new ListChangedEventHandler(Sessions_ListChanged);
            SuperPuTTY.Settings.SettingsSaving += new SettingsSavingEventHandler(Settings_SettingsSaving);
        }

        void ExpandInitialTree()
        {
            if (SuperPuTTY.Settings.ExpandSessionsTreeOnStartup)
            {
                nodeRoot.ExpandAll();
                this.treeView1.SelectedNode = nodeRoot;
            }
            else
            {
                // start with semi-collapsed view
                nodeRoot.Expand();
                foreach (TreeNode node in this.nodeRoot.Nodes)
                {
                    if (!IsSessionNode(node))
                    {
                        node.Collapse();
                    }
                }
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
        }

        void Settings_SettingsSaving(object sender, CancelEventArgs e)
        {
            this.ApplySettings();
        }

        void ApplySettings()
        {
            this.treeView1.ShowLines = SuperPuTTY.Settings.SessionsTreeShowLines;
            this.treeView1.Font = SuperPuTTY.Settings.SessionsTreeFont;
            this.panelSearch.Visible = SuperPuTTY.Settings.SessionsShowSearch;
            this.ResortNodes();
        }

        protected override void OnClosed(EventArgs e)
        {
            ReleaseSessionSubscriptions();
            base.OnClosed(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel && !FlushPendingSessionSave())
            {
                e.Cancel = true;
                MessageBox.Show(this, "The moved sessions could not be saved.\n\n" + sessionSaveError,
                    "Could Not Save Sessions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SubscribeSession(SessionData session)
        {
            if (!subscribedSessions.Add(session)) return;
            session.OnPropertyChanged += SessionPropertyChanged;
            session.OnPropertyChanging += SessionPropertyChanging;
        }

        private void DetachSessionEvents()
        {
            foreach (var session in subscribedSessions)
            {
                session.OnPropertyChanged -= SessionPropertyChanged;
                session.OnPropertyChanging -= SessionPropertyChanging;
            }
            subscribedSessions.Clear();
        }

        private void ReleaseSessionSubscriptions()
        {
            DetachSessionEvents();
            SuperPuTTY.Sessions.ListChanged -= Sessions_ListChanged;
            SuperPuTTY.Settings.SettingsSaving -= Settings_SettingsSaving;
        }

        void LoadSessions(bool preserveEmptyFolders = false)
        {
            string selectedId = SelectedSession == null
                ? treeView1.SelectedNode == null ? null : GetNodePath(treeView1.SelectedNode)
                : SelectedSession.SessionId;
            var expandedPaths = GetExpandedPaths(nodeRoot).ToList();
            var leaves = new List<TreeNode>();
            if (preserveEmptyFolders) GetAllNodes(nodeRoot, leaves);
            var emptyFolders = leaves.Where(IsFolderNode).Select(GetNodePath).ToList();
            rebuildingTree = true;
            treeView1.BeginUpdate();
            try
            {
                DetachSessionEvents();
                treeView1.Nodes.Clear();
                this.nodeRoot = treeView1.Nodes.Add("root", "PuTTY Sessions", ImageKeyFolder, ImageKeyFolder);
                this.nodeRoot.ContextMenuStrip = this.contextMenuStripFolder;
                foreach (SessionData session in SuperPuTTY.GetAllSessions()) TryAddSessionNode(session);
                RestoreEmptyFolders(emptyFolders);
                ResortNodes();
                RestoreExpandedPaths(expandedPaths);
                if (selectedId != null) treeView1.SelectedNode = FindNode(selectedId);
            }
            finally
            {
                treeView1.EndUpdate();
                rebuildingTree = false;
            }
            SelectionChanged?.Invoke(SelectedSession);
        }

        private void TryAddSessionNode(SessionData session)
        {
            SubscribeSession(session);
            TreeNode nodeParent = this.nodeRoot;
            if (this.filter == null || this.filter(session))
            {
                if (session.SessionId != null && session.SessionId != session.SessionName)
                {
                    // take session id and create folder nodes
                    nodeParent = FindOrCreateParentNode(session.SessionId);
                }
                AddSessionNode(nodeParent, session, true);
            }
        }

        public void SessionPropertyChanged(SessionData Session, String PropertyName)
        {
            if (Session == null || IsDisposed || Disposing || SuperPuTTY.IsUpdatingSessionCatalog)
                return;

            if (PropertyName == "SessionName" || PropertyName == "ImageKey")
            {
                TreeNode Node = FindSessionNode(Session.SessionId);
                if (Node == null)
                {
                    // It is possible that the session id was changed before the
                    // session name. In this case, we check to see if we
                    // can find a node with the old session id that is also associated
                    // to the session data.
                    Node = FindSessionNode(Session.OldSessionId);
                    if (Node == null || Node.Tag != Session)
                        return;
                }

                Node.Text = Session.SessionName;
                Node.Name = Session.SessionName;
                Node.ImageKey = Session.ImageKey;
                Node.SelectedImageKey = Session.ImageKey;

                bool IsSelectedSession = treeView1.SelectedNode == Node;
                ResortNodes();
                if (IsSelectedSession)
                {
                    // Re-selecting the node since it will be unselected when sorting.
                    treeView1.SelectedNode = Node;
                }
            }
            else if (PropertyName == "SessionId" && !SuperPuTTY.IsUpdatingSessionCatalog)
            {
                TreeNode Node = FindSessionNode(Session.OldSessionId);
                if (Node == null)
                {
                    // It is possible that the session name was changed before the
                    // session id. In this case, we check to see if we
                    // can find a node with the current session id that is also associated
                    // to the session data.
                    Node = FindSessionNode(Session.SessionId);
                    if (Node == null || Node.Tag != Session)
                        return;
                }

                try
                {
                    this.isRenamingNode = true;
                    SuperPuTTY.RemoveSession(Session.OldSessionId);
                    SuperPuTTY.AddSession(Session);
                }
                finally
                {
                    this.isRenamingNode = false;
                }
            }
        }

        public void SessionPropertyChanging(SessionData Session, String AttributeName, Object NewValue, ref bool CancelChange)
        {
            if (Session == null || IsDisposed || Disposing || SuperPuTTY.IsUpdatingSessionCatalog)
                return;

            if (AttributeName == "SessionName")
            {
                TreeNode Node = FindSessionNode(Session.SessionId);
                if (Node == null)
                    return;

                String Error = "";
                bool IsValid = ValidateSessionNameChange(Node.Parent, Node, NewValue as String, out Error);
                if (!IsValid)
                {
                    CancelChange = true;
                    MessageBox.Show(Error);
                }
            }
        }

        void Sessions_ListChanged(object sender, ListChangedEventArgs e)
        {
            if (isRenamingNode)
            {
                return;
            }
            BindingList<SessionData> sessions = (BindingList<SessionData>) sender;
            if (e.ListChangedType == ListChangedType.ItemAdded)
            {
                SessionData session = sessions[e.NewIndex];
                TryAddSessionNode(session);
            }
            else if (e.ListChangedType == ListChangedType.Reset || e.ListChangedType == ListChangedType.ItemDeleted)
            {
                LoadSessions(e is SuperPuTTY.SessionSaveResetEventArgs);
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (!rebuildingTree && this.SelectionChanged != null)
            {
                this.SelectionChanged(this.SelectedSession);
            }
        }

        /// <summary>
        /// Opens the selected session when the node is double clicked in the treeview
        /// </summary>
        /// <param name="sender">The treeview control that was double clicked</param>
        /// <param name="e">An Empty EventArgs object</param>
        private void treeView1_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            // e is null if this method is called from connectToolStripMenuItem_Click
            TreeNode node = e != null ? e.Node : treeView1.SelectedNode;

            if (IsSessionNode(node) && node == treeView1.SelectedNode)
            {
                SessionData sessionData = (SessionData)node.Tag;
                SuperPuTTY.OpenSession(new SessionDataStartInfo { Session = sessionData });
            }
        }


        /// <summary>
        /// Create/Update a session entry
        /// </summary>
        /// <param name="sender">The toolstripmenuitem control that was clicked</param>
        /// <param name="e">An Empty EventArgs object</param>
        private void CreateOrEditSessionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionData session = null;
            TreeNode node = null;
            TreeNode nodeRef = this.nodeRoot;
            string title = null;
            if (sender is ToolStripMenuItem)
            {
                ToolStripMenuItem menuItem = (ToolStripMenuItem)sender;
                bool isFolderNode = IsFolderNode(treeView1.SelectedNode);
                if (menuItem.Text.ToLower().Equals("new") || isFolderNode)
                {
                    session = new SessionData();
                    nodeRef = isFolderNode ? treeView1.SelectedNode : treeView1.SelectedNode.Parent;
                    title = "Create New Session";
                }
                else if (menuItem == this.createLikeToolStripMenuItem)
                {
                    // copy as
                    session = (SessionData) ((SessionData) treeView1.SelectedNode.Tag).Clone();
                    session.SessionId = SuperPuTTY.MakeUniqueSessionId(session.SessionId);
                    session.SessionName = SessionData.GetSessionNameFromId(session.SessionId);
                    nodeRef = treeView1.SelectedNode.Parent;
                    title = "Create New Session Like " + session.OldName;
                }
                else
                {
                    // edit, session node selected
                    // We make a clone of the session since we do not want to directly edit the real object.
                    session = (SessionData)((SessionData)treeView1.SelectedNode.Tag).Clone();
                    node = treeView1.SelectedNode;
                    nodeRef = node.Parent;
                    title = "Edit Session: " + session.SessionName;
                }
            }

            dlgEditSession form = new dlgEditSession(session, this.treeView1.ImageList) {Text = title};
            form.SessionNameValidator += delegate(string txt, out string error)
            {
                bool IsValid = ValidateSessionNameChange(nodeRef, node, txt, out error);
                return IsValid;
            };
            
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                SessionData savedSession = node == null ? session : (SessionData)node.Tag;
                // A new session's parent is selected in the tree, rather than in the editor.
                if (node == null && nodeRoot != nodeRef)
                {
                    UpdateSessionId(nodeRef, session);
                    session.SessionId = SessionData.CombineSessionIds(session.SessionId, session.SessionName);
                }

                try
                {
                    SuperPuTTY.SaveSessionChanges(savedSession, session, SuperPuTTY.SaveSessions);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException ||
                    ex is System.IO.IOException || ex is UnauthorizedAccessException)
                {
                    Log.Warn("Could not save the edited session; its changes were rolled back.", ex);
                    MessageBox.Show(this, "The session could not be saved.\n\n" + ex.Message,
                        "Could Not Save Session", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Saving rebuilds the tree; select the replacement node by its final ID.
                this.treeView1.SelectedNode = FindSessionNode(savedSession.SessionId);
            }
            
        }

        private void RestoreEmptyFolders(IEnumerable<string> paths)
        {
            // Empty folders exist only in the tree and are retained across session-save resets.
            foreach (string path in paths)
            {
                TreeNode parent = nodeRoot;
                string prefix = String.Empty;
                foreach (string part in path.Split('/'))
                {
                    prefix = prefix.Length == 0 ? part : prefix + "/" + part;
                    if (SuperPuTTY.GetSessionById(prefix) != null) break;
                    parent = parent.Nodes[part] ?? AddFolderNode(parent, part);
                }
            }
        }

        private bool ValidateSessionNameChange(TreeNode ParentNode, TreeNode Node, String NewName, out String Error)
        {
            if (String.IsNullOrWhiteSpace(NewName))
            {
                Error = "Empty name";
                return false;
            }
            bool IsEdit = Node != null;
            Error = String.Empty;
            bool IsDupeNode;
            if (IsEdit)
                IsDupeNode = ParentNode.Nodes.ContainsKey(NewName) && ParentNode.Nodes[NewName] != Node;
            else
                IsDupeNode = ParentNode.Nodes.ContainsKey(NewName);

            string path = CombineNodePath(ParentNode, NewName);
            var session = Node == null ? null : Node.Tag as SessionData;
            bool sameSession = session != null && String.Equals(session.SessionId, path, StringComparison.OrdinalIgnoreCase);
            if (IsDupeNode || (!sameSession && SuperPuTTY.SessionPathExists(path)))
            {
                Error = "Session with same name exists";
            }
            else if (NewName.Contains(SessionIdDelim))
            {
                Error = "Invalid character ( " + SessionIdDelim + " ) in name";
            }
            return string.IsNullOrEmpty(Error);
        }

        /// <summary>
        /// Forces a node to be selected when right clicked, this assures the context menu will be operating
        /// on the proper session entry.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                treeView1.SelectedNode = treeView1.GetNodeAt(e.X, e.Y);
            }          
        }

        /// <summary>
        /// Delete a session entry from the treeview and the registry
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionData session = (SessionData)treeView1.SelectedNode.Tag;
            if (MessageBox.Show("Are you sure you want to delete " + session.SessionName + "?", "Delete Session?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                //session.RegistryRemove(session.SessionName);
                treeView1.SelectedNode.Remove();
                SuperPuTTY.RemoveSession(session.SessionId);
                SuperPuTTY.SaveSessions();
                //m_SessionsById.Remove(session.SessionId);
            }
        }

        /// <summary>
        /// Open a directory listing on the selected nodes host to allow dropping files
        /// for drag + drop copy.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void fileBrowserToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionData session = (SessionData)treeView1.SelectedNode.Tag;
            SuperPuTTY.OpenScpSession(session);
        }

        /// <summary>
        /// Shortcut for double clicking an entries node.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void connectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            treeView1_NodeMouseDoubleClick(null, null);
        }

        /// <summary>
        /// Open putty with args but as external process
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void connectExternalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (IsSessionNode(node))
            {
                SessionData sessionData = (SessionData)node.Tag;
                PuttyStartInfo startInfo = new PuttyStartInfo(sessionData);
                startInfo.StartStandalone();
            }
        }

        private void pingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!WCMDStartInfo.CanPing(this.SelectedSession))
                return;

            SessionData session = WCMDStartInfo.CreatePingSession(this.SelectedSession,
                sender == this.pingContinuouslyToolStripMenuItem);
            if (sender == this.pingExternallyToolStripMenuItem)
                new PuttyStartInfo(session).StartStandalone();
            else
                SuperPuTTY.OpenSession(new SessionDataStartInfo { Session = session });
        }

        private void connectInNewSuperPuTTYToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (IsSessionNode(node))
            {
                SuperPuTTY.LoadSessionInNewInstance(((SessionData)node.Tag).SessionId);
            }
        }

        private void newFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node != null)
            {
                dlgRenameItem dialog = new dlgRenameItem
                {
                    Text = "New Folder",
                    ItemName = "New Folder",
                    DetailName = "",
                    ItemNameValidator = delegate(string txt, out string error)
                    {
                        error = String.Empty;
                        if (node.Nodes.ContainsKey(txt) || SuperPuTTY.SessionPathExists(CombineNodePath(node, txt)))
                        {
                            error = "Node with same name exists";
                        }
                        else if (txt.Contains(SessionIdDelim))
                        {
                            error = "Invalid character ( " + SessionIdDelim + " ) in name";
                        }
                        else if (string.IsNullOrEmpty(txt) || txt.Trim() == String.Empty)
                        {
                            error = "Empty folder name";
                        }

                        return string.IsNullOrEmpty(error);
                    }
                };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddFolderNode(node, dialog.ItemName);
                }
            }
        }

        private void renameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node != null)
            {
                dlgRenameItem dialog = new dlgRenameItem
                {
                    Text = "Rename Folder",
                    ItemName = node.Text,
                    DetailName = "",
                    ItemNameValidator = delegate(string txt, out string error)
                    {
                        error = String.Empty;
                        if (String.IsNullOrWhiteSpace(txt))
                            error = "Empty folder name";
                        else if ((node.Parent.Nodes.ContainsKey(txt) && !String.Equals(txt, node.Text, StringComparison.OrdinalIgnoreCase)) ||
                            (!String.Equals(txt, node.Text, StringComparison.OrdinalIgnoreCase) && SuperPuTTY.SessionPathExists(CombineNodePath(node.Parent, txt))))
                        {
                            error = "Node with same name exists";
                        }
                        else if (txt.Contains(SessionIdDelim))
                        {
                            error = "Invalid character ( " + SessionIdDelim + " ) in name";
                        }
                        return string.IsNullOrEmpty(error);
                    }
                };
                if (dialog.ShowDialog(this) == DialogResult.OK && node.Text != dialog.ItemName)
                {
                    try
                    {
                        RenameFolderNode(node, dialog.ItemName);
                        SuperPuTTY.SaveSessions();
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
                    {
                        MessageBox.Show(this, ex.Message, "Rename Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void removeFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = treeView1.SelectedNode;
            if (node == null || node == nodeRoot) return;
            string folder = GetNodePath(node);
            var sessions = SuperPuTTY.GetSessionsInFolder(folder);
            if (sessions.Count == 0) { node.Remove(); return; }
            if (MessageBox.Show("Remove Folder [" + node.Text + "] and [" + sessions.Count + "] sessions?",
                "Remove Folder?", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            SuperPuTTY.RemoveSessionFolder(folder);
            SuperPuTTY.SaveSessions();
            SuperPuTTY.ReportStatus("Removed Folder, {0} and {1} sessions", folder, sessions.Count);
        }

        private void connectAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node != null && !IsSessionNode(node))
            {
                List<SessionData> sessions = new List<SessionData>();
                GetAllSessions(node, sessions);
                Log.InfoFormat("Found {0} sessions", sessions.Count);

                if (sessions.Count > ConnectAllConfirmationThreshold)
                {
                    if (DialogResult.Cancel == MessageBox.Show(
                        "Open All " + sessions.Count + " sessions?", 
                        "WARNING", 
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Warning))
                    {
                        // The user canceled the batch; confirming opens every session.
                        return;
                    }
                }
                foreach (SessionData session in sessions)
                {
                    SuperPuTTY.OpenSession(new SessionDataStartInfo { Session = session });
                }
            }
        }

        private void contextMenuStripFolder_Opening(object sender, CancelEventArgs e)
        {
            bool isRootNode = this.treeView1.SelectedNode != this.nodeRoot;
            this.renameToolStripMenuItem.Enabled = isRootNode;
            // TODO: handle removing folder and nodes in it recursively
            this.removeFolderToolStripMenuItem.Enabled = isRootNode;// && this.treeView1.SelectedNode.Nodes.Count == 0;
        }

        private void copyHostToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionData session = this.SelectedSession;
            if (session == null || String.IsNullOrWhiteSpace(session.Host)) return;
            try
            {
                Clipboard.SetDataObject(session.Host, true, 5, 100);
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                Log.Warn("Unable to copy session host to clipboard", ex);
                MessageBox.Show(this, "The clipboard is busy. Please try copying again.",
                    "Copy IP/Hostname", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void contextMenuStripAddTreeItem_Opening(object sender, CancelEventArgs e)
        {
            SessionData selectedSession = this.SelectedSession;
            this.copyHostToolStripMenuItem.Enabled = selectedSession != null && !String.IsNullOrWhiteSpace(selectedSession.Host);
            bool isScpSession = selectedSession != null && selectedSession.Proto == ConnectionProtocol.SCP;

            bool canPing = WCMDStartInfo.CanPing(selectedSession);
            string pingLabel = "Ping" + (canPing ? " " + selectedSession.Host.Trim() : "");
            this.pingToolStripMenuItem.Text = pingLabel;
            this.pingContinuouslyToolStripMenuItem.Text = pingLabel + " continuously";
            this.pingExternallyToolStripMenuItem.Text = pingLabel + " externally";
            this.pingToolStripMenuItem.Enabled = canPing;
            this.pingContinuouslyToolStripMenuItem.Enabled = canPing;
            this.pingExternallyToolStripMenuItem.Enabled = canPing;

            // disable file transfers if pscp isn't configured.
            fileBrowserToolStripMenuItem.Enabled = SuperPuTTY.IsScpEnabled;
            this.fileZillaToolStripMenuItem.Enabled = SuperPuTTY.IsFilezillaEnabled;
            this.winSCPToolStripMenuItem.Enabled = SuperPuTTY.IsWinSCPEnabled;
            this.fileZillaToolStripMenuItem.Visible = SuperPuTTY.IsFilezillaEnabled;
            this.winSCPToolStripMenuItem.Visible = SuperPuTTY.IsWinSCPEnabled;

            connectExternalToolStripMenuItem.Enabled = !isScpSession;
            connectInNewSuperPuTTYToolStripMenuItem.Enabled = !SuperPuTTY.Settings.SingleInstanceMode;
        }

        #region Node helpers

        TreeNode AddSessionNode(TreeNode parentNode, SessionData session, bool isInitializing)
        {
            TreeNode addedNode = null;
            if (parentNode.Nodes.ContainsKey(session.SessionName))
            {
                SuperPuTTY.ReportStatus("Node with the same name exists.  New node ({0}) NOT added", session.SessionName);
            }
            else
            {
                addedNode = parentNode.Nodes.Add(session.SessionName, session.SessionName, ImageKeySession, ImageKeySession);
                addedNode.Tag = session;
                addedNode.ContextMenuStrip = this.contextMenuStripAddTreeItem;
                addedNode.ToolTipText = session.ToString();

                // Override with custom icon if valid
                if (IsValidImage(session.ImageKey))
                {
                    addedNode.ImageKey = session.ImageKey;
                    addedNode.SelectedImageKey = session.ImageKey;
                }
            }

            return addedNode;
        }

        TreeNode AddFolderNode(TreeNode parentNode, String nodeName)
        {
            TreeNode nodeNew = null;
            if (parentNode.Nodes.ContainsKey(nodeName))
            {
                SuperPuTTY.ReportStatus("Node with the same name exists.  New node ({0}) NOT added", nodeName);
            }
            else
            {
                SuperPuTTY.ReportStatus("Adding new folder, {1}.  parent={0}", parentNode.Text, nodeName);
                nodeNew = parentNode.Nodes.Add(nodeName, nodeName, ImageKeyFolder, ImageKeyFolder);
                nodeNew.ContextMenuStrip = this.contextMenuStripFolder;
            }
            return nodeNew;
        }

        bool IsSessionNode(TreeNode node)
        {
            return node != null && node.Tag is SessionData;
        }

        bool IsFolderNode(TreeNode node)
        {
            return node != null && !IsSessionNode(node);
        }

        private void UpdateSessionId(TreeNode parentNode)
        {
            foreach (TreeNode node in parentNode.Nodes)
            {
                if (IsSessionNode(node))
                {
                    UpdateSessionId(node, (SessionData)node.Tag);
                }
                else
                {
                    UpdateSessionId(node);
                }
            }
        }
        private void UpdateSessionId(TreeNode addedNode, SessionData session)
        {
            // set session id as node path
            List<string> parentNodeNames = new List<string>();
            for (TreeNode node = addedNode; node.Parent != null; node = node.Parent)
            {
                parentNodeNames.Add(node.Text);
            }
            parentNodeNames.Reverse();
            String sessionId = String.Join(SessionIdDelim, parentNodeNames.ToArray());
            //Log.InfoFormat("sessionId={0}", sessionId);
            //session.SessionId = sessionId;
            if (session != null) session.SessionId = sessionId;
            //SuperPuTTY.SaveSessions();
            //session.SaveToRegistry();
        }

        TreeNode FindOrCreateParentNode(string sessionId)
        {
            Log.DebugFormat("Finding Parent Node for sessionId ({0})", sessionId);
            TreeNode nodeParent = this.nodeRoot;

            string[] parts = sessionId.Split(SessionIdDelim.ToCharArray());
            if (parts.Length > 1)
            {
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    string part = parts[i];
                    TreeNode node = nodeParent.Nodes[part];
                    if (node == null)
                    {
                        nodeParent = this.AddFolderNode(nodeParent, part);
                    }
                    else if (IsFolderNode(node))
                    {
                        nodeParent = node;
                    }
                }
            }

            Log.DebugFormat("Returning node ({0})", nodeParent.Text);
            return nodeParent;
        }

        TreeNode FindSessionNode(String SessionId)
        {
            Log.DebugFormat("Finding Node for sessionId ({0})", SessionId);
            TreeNode CurrentNode = this.nodeRoot;
            TreeNode NodeToReturn = null;

            if (String.IsNullOrEmpty(SessionId)) return null;
            string[] Parts = SessionId.Split(SessionIdDelim.ToCharArray());
            if (Parts.Length > 0)
            {
                for (int i = 0; i < Parts.Length; i++)
                {
                    string Part = Parts[i];
                    CurrentNode = CurrentNode.Nodes[Part];
                    if (CurrentNode == null)
                    {
                        return null;
                    }
                    else if (i == Parts.Length - 1 && !IsFolderNode(CurrentNode))
                    {
                        NodeToReturn = CurrentNode;
                    }
                }
            }

            Log.DebugFormat("Returning node ({0})", NodeToReturn != null ? NodeToReturn.Text : "null");
            return NodeToReturn;
        }

        public int Compare(object x, object y)
        {
            TreeNode tx = x as TreeNode;
            TreeNode ty = y as TreeNode;
            if (SuperPuTTY.Settings.SessiontreeShowFoldersFirst) { 
                if (IsFolderNode(tx) && IsSessionNode(ty)) {
                    return -1;
                }
            }
            return string.Compare(tx.Text, ty.Text);

        }

        void ResortNodes()
        {
            this.treeView1.TreeViewNodeSorter = null;
            this.treeView1.TreeViewNodeSorter = this;
        }


        private void expandAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
              TreeNode node = this.treeView1.SelectedNode;
              if (node != null)
              {
                  node.ExpandAll();
              }
        }

        private void collapseAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node != null)
            {
                node.Collapse();
                if (node == this.nodeRoot)
                {
                    nodeRoot.Expand();
                }
            }
        }

        void GetAllSessions(TreeNode nodeFolder, List<SessionData> sessions)
        {
            if (nodeFolder != null)
            {
                foreach (TreeNode child in nodeFolder.Nodes)
                {
                    if (IsSessionNode(child))
                    {
                        SessionData session = (SessionData) child.Tag;
                        sessions.Add(session);
                    }
                    else
                    {
                        GetAllSessions(child, sessions);
                    }
                }
            }
        }

        void GetAllNodes(TreeNode node, List<TreeNode> nodes)
        {
            if (node != null)
            {
                foreach (TreeNode child in node.Nodes)
                {
                    if (child.Nodes.Count == 0)
                    {
                        nodes.Add(child);
                    }
                    else
                    {
                        GetAllNodes(child, nodes);
                    }
                }
            }
        }
        #endregion

        #region Drag Drop

        private void treeView1_ItemDrag(object sender, ItemDragEventArgs e)
        {
            // Get the tree
            TreeView tree = (TreeView)sender;

            // Get the node underneath the mouse.
            TreeNode node = e.Item as TreeNode;

            // Start the drag-and-drop operation with a cloned copy of the node.
            //if (node != null && IsSessionNode(node))
            if (node != null && tree.Nodes[0] != node)
            {
                this.treeView1.DoDragDrop(node, DragDropEffects.Copy);
            }
        }

        private void treeView1_DragOver(object sender, DragEventArgs e)
        {
            // Get the tree.
            TreeView tree = (TreeView)sender;

            // Drag and drop denied by default.
            e.Effect = DragDropEffects.None;

            // Is it a valid format?
            TreeNode nodePayload = (TreeNode) e.Data.GetData(typeof(TreeNode));
            if (nodePayload != null)
            {
                // Get the screen point.
                Point pt = new Point(e.X, e.Y);

                // Convert to a point in the TreeView's coordinate system.
                pt = tree.PointToClient(pt);

                TreeNode node = tree.GetNodeAt(pt);
                // Is the mouse over a valid node?
                if (node != null && node != nodePayload && !IsDescendantOf(node, nodePayload))
                {
                    tree.SelectedNode = node;
                    // folder that is not the same parent and new node name is not already present
                    if (IsFolderNode(node) && node != nodePayload.Parent && !node.Nodes.ContainsKey(nodePayload.Text) &&
                        !SuperPuTTY.SessionPathExists(CombineNodePath(node, nodePayload.Text)))
                    {
                        e.Effect = DragDropEffects.Copy;
                    }
                }
            }
        }

        private void treeView1_DragDrop(object sender, DragEventArgs e)
        {
            // Get the tree.
            TreeView tree = (TreeView)sender;

            // Get the screen point.
            Point pt = new Point(e.X, e.Y);

            // Convert to a point in the TreeView's coordinate system.
            pt = tree.PointToClient(pt);

            // Get the node underneath the mouse.
            TreeNode node = tree.GetNodeAt(pt);

            TreeNode payload = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            try
            {
                if (MoveSessionNode(payload, node))
                {
                    ScheduleSessionSave();
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                MessageBox.Show(this, ex.Message, "Move Session", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static bool IsDescendantOf(TreeNode node, TreeNode ancestor)
        {
            for (var current = node; current != null; current = current.Parent)
                if (Object.ReferenceEquals(current, ancestor)) return true;
            return false;
        }

        private static string GetNodePath(TreeNode node)
        {
            var parts = new List<string>();
            for (var current = node; current != null && current.Parent != null; current = current.Parent)
                parts.Insert(0, current.Text);
            return String.Join("/", parts);
        }

        private static string CombineNodePath(TreeNode parent, string name)
        {
            string prefix = GetNodePath(parent);
            return prefix.Length == 0 ? name : prefix + "/" + name;
        }

        private TreeNode FindNode(string path)
        {
            TreeNode node = nodeRoot;
            if (String.IsNullOrEmpty(path)) return node;
            foreach (string part in path.Split('/'))
            {
                node = node.Nodes[part];
                if (node == null) return null;
            }
            return node;
        }

        private static IEnumerable<string> GetExpandedPaths(TreeNode node)
        {
            if (node == null) yield break;
            if (node.IsExpanded) yield return GetNodePath(node);
            foreach (TreeNode child in node.Nodes)
                foreach (string path in GetExpandedPaths(child)) yield return path;
        }

        private void RestoreExpandedPaths(IEnumerable<string> paths)
        {
            foreach (string path in paths) FindNode(path)?.Expand();
        }

        internal void RenameFolderNode(TreeNode node, string name)
        {
            if (!IsFolderNode(node) || node == nodeRoot || String.IsNullOrWhiteSpace(name) || name.Contains("/"))
                throw new ArgumentException("A valid folder name is required.");
            if (node.Parent.Nodes.ContainsKey(name) && node.Parent.Nodes[name] != node)
                throw new InvalidOperationException("A node with the same name exists.");
            string path = CombineNodePath(node.Parent, name);
            string oldPath = GetNodePath(node);
            var expandedPaths = GetExpandedPaths(node).Select(p => path + p.Substring(oldPath.Length)).ToList();
            if (SuperPuTTY.MoveSessionFolder(oldPath, path) == 0)
            {
                node.Text = name; node.Name = name;
            }
            RestoreExpandedPaths(expandedPaths);
            treeView1.SelectedNode = FindNode(path);
            ResortNodes();
        }

        internal bool MoveSessionNode(TreeNode payload, TreeNode target)
        {
            if (payload == null || payload == nodeRoot || !IsFolderNode(target) ||
                payload.Parent == target || IsDescendantOf(target, payload)) return false;
            string path = CombineNodePath(target, payload.Text);
            if (target.Nodes.ContainsKey(payload.Text) || SuperPuTTY.SessionPathExists(path))
                throw new InvalidOperationException("A node with the same name exists at the destination.");
            string oldPath = GetNodePath(payload);
            var expandedPaths = GetExpandedPaths(payload).Select(p => path + p.Substring(oldPath.Length)).ToList();
            if (IsSessionNode(payload))
                SuperPuTTY.MoveSavedSession((SessionData)payload.Tag, path);
            else if (SuperPuTTY.MoveSessionFolder(oldPath, path) == 0)
            {
                payload.Remove(); target.Nodes.Add(payload);
            }
            RestoreExpandedPaths(expandedPaths);
            treeView1.SelectedNode = FindNode(path);
            treeView1.SelectedNode?.EnsureVisible();
            return true;
        }

        #endregion

        public void resetFoldersChildrenPaths(TreeNode nodePayload)
        {
            // A caller may already have moved the displayed folder. Recover its old
            // model prefix from a descendant, including siblings hidden by filtering.
            var leaves = new List<TreeNode>();
            GetAllNodes(nodePayload, leaves);
            var leaf = leaves.FirstOrDefault(IsSessionNode);
            string path = GetNodePath(nodePayload);
            if (leaf != null)
            {
                var session = (SessionData)leaf.Tag;
                string suffix = GetNodePath(leaf).Substring(path.Length);
                if (suffix.Length > 0 && session.SessionId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                    Object.ReferenceEquals(SuperPuTTY.GetSessionById(session.SessionId), session))
                {
                    SuperPuTTY.MoveSessionFolder(session.SessionId.Substring(0, session.SessionId.Length - suffix.Length), path);
                    return;
                }
            }
            UpdateSessionId(nodePayload);
        }

        private void timerDelayedSave_Tick(object sender, EventArgs e)
        {
            timerDelayedSave.Stop();
            if (FlushPendingSessionSave())
                SuperPuTTY.ReportStatus("Saved Sessions after Drag-Drop @ {0}", DateTime.Now);
            else
                SuperPuTTY.ReportStatus("Moved sessions have not been saved: {0}", sessionSaveError);
        }

        internal void ScheduleSessionSave()
        {
            sessionSavePending = true;
            timerDelayedSave.Stop();
            timerDelayedSave.Start();
        }

        internal bool FlushPendingSessionSave()
        {
            if (!sessionSavePending) return true;
            try
            {
                SuperPuTTY.SaveSessions();
                sessionSavePending = false;
                timerDelayedSave.Stop();
                sessionSaveError = null;
                return true;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException ||
                ex is ArgumentException || ex is InvalidOperationException || ex is System.Security.SecurityException)
            {
                sessionSaveError = ex.Message;
                Log.Warn("Moved sessions could not be saved; changes remain pending.", ex);
                return false;
            }
        }

        #region Icon
        bool IsValidImage(string imageKey)
        {
            bool valid = false;
            if (!string.IsNullOrEmpty(imageKey))
            {
                valid = this.treeView1.ImageList.Images.ContainsKey(imageKey);
                if (!valid)
                {
                    Log.WarnFormat("Missing icon, {0}", imageKey);
                }
            }
            return valid;
        }


        #endregion

        #region Search
        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    if (SuperPuTTY.Settings.FilterSessionsOnChange)
                    {
                        treeView1.Focus();
                    }
                    else
                    {
                        this.ApplySearch(this.txtSearch.Text);
                    }
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Escape:
                    this.ClearSearch();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;
            }
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            this.ApplySearch(this.txtSearch.Text);
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            this.ClearSearch();
        }

        private void ClearSearch()
        {
            this.txtSearch.Text = "";
            this.ApplySearch("");
        }
        private void ApplySearch(string txt)
        {
            Log.InfoFormat("Applying Search: txt={0}.", txt);

            // define filter
            SearchFilter searchFilter = new SearchFilter(SuperPuTTY.Settings.SessionsSearchMode, txt);
            this.filter = searchFilter.IsMatch;

            // reload
            this.LoadSessions();

            // if "clear" show init state otherwise expand all to show all matches
            if (string.IsNullOrEmpty(txt))
            {
                this.ExpandInitialTree();
            }
            else
            {
                this.treeView1.ExpandAll();
            }
        }

        public enum SearchMode
        {
            CaseSensitive, CaseInSensitive, Regex
        }

        public class SearchFilter
        {
            public SearchFilter(string mode, string filter)
            {
                this.Mode = FormUtils.SafeParseEnum(mode, true, SearchMode.CaseSensitive);
                this.Filter = filter ?? String.Empty;
                if (this.Mode == SearchMode.Regex)
                {
                    try
                    {
                        this.Regex = new Regex(this.Filter, RegexOptions.None, TimeSpan.FromMilliseconds(100));
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Could not parse pattern: " + filter, ex);
                    }
                }
            }
            public bool IsMatch(SessionData s)
            {
                if (s == null) return false;
                string name = s.SessionName ?? String.Empty;
                string host = s.Host ?? String.Empty;
                string filter = this.Filter ?? String.Empty;
                if (this.Mode == SearchMode.Regex)
                {
                    try { return this.Regex == null || this.Regex.IsMatch(name); }
                    catch (RegexMatchTimeoutException)
                    {
                        // Disable this expression for the rest of the search so many sessions
                        // cannot each consume another timeout on the UI thread.
                        Log.Warn("Session search expression exceeded its time limit and was disabled.");
                        this.Regex = null;
                        return true;
                    }
                }
                var comparison = this.Mode == SearchMode.CaseInSensitive
                    ? StringComparison.CurrentCultureIgnoreCase : StringComparison.Ordinal;
                return name.IndexOf(filter, comparison) >= 0 || host.IndexOf(filter, comparison) >= 0;
            }
            public SearchMode Mode { get; set; }
            public string Filter { get; set; }
            public Regex Regex { get; set; }
        }
        #endregion

        #region Key Handling
        private void treeView1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)13 && IsSessionNode(this.treeView1.SelectedNode))
            {
                if (Control.ModifierKeys == Keys.None)
                {
                    treeView1_NodeMouseDoubleClick(null, null);
                    e.Handled = true;
                }
                else if (Control.ModifierKeys == Keys.Shift)
                {
                    CreateOrEditSessionToolStripMenuItem_Click(this.settingsToolStripMenuItem, e);
                    e.Handled = true;
                }
            }
        }
        #endregion

        private void winSCPToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionData session = (SessionData)treeView1.SelectedNode.Tag;
            ExternalApplications.openWinSCP(session);
        }

        private void fileZillaToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionData session = (SessionData)treeView1.SelectedNode.Tag;
            ExternalApplications.openFileZilla(session); 
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            if (SuperPuTTY.Settings.FilterSessionsOnChange)
            {
                this.ApplySearch(this.txtSearch.Text);
            }
        }
    }

}
