using System.Windows.Forms;
using SuperPutty.Data;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty
{
    /// <summary>
    /// A session tab in the workspace.
    /// </summary>
    public partial class ToolWindowDocument : ToolWindow
    {
        public ToolWindowDocument()
        {
            InitializeComponent();
            DockStateChanged += (sender, e) =>
            {
                if (SessionDockingHistory.IsDockPosition(DockState)) LastDockPosition = DockState;
            };
        }

        internal SessionData SavedSession { get; private set; }
        internal string SessionWorkspace { get; private set; }
        internal DockState LastDockPosition { get; private set; } = DockState.Unknown;

        internal void AssociateSavedSession(SessionData session)
        {
            if (session == null || !object.ReferenceEquals(SuperPuTTY.GetSessionById(session.SessionId), session))
                return;
            SavedSession = session;
            SessionWorkspace = SuperPuTTY.Settings.SettingsFolder;
        }

        internal SessionData GetCurrentSavedSession()
        {
            return SavedSession != null &&
                Utils.SettingsFolderResolver.PathsEqual(SessionWorkspace, SuperPuTTY.Settings.SettingsFolder) &&
                object.ReferenceEquals(SuperPuTTY.GetSessionById(SavedSession.SessionId), SavedSession)
                ? SavedSession : null;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SuperPuTTY.MainForm?.RememberSessionDocking(this);
            base.OnFormClosed(e);
            if (this.DockHandler.Pane != null)
            {
                int idx = this.DockHandler.Pane.Contents.IndexOf(this);
                if (idx > 0)
                {
                    IDockContent contentToActivate = this.DockHandler.Pane.Contents[idx - 1];
                    contentToActivate.DockHandler.Activate();
                }
            }
        }
    }
}
