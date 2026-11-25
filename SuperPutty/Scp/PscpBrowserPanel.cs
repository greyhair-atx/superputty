using System;
using System.IO;
using System.Threading.Tasks;
using SuperPutty.Data;

namespace SuperPutty.Scp
{
    public partial class PscpBrowserPanel : ToolWindowDocument
    {
        FileTransferPresenter fileTransferPresenter;
        IBrowserPresenter localBrowserPresenter;
        BrowserPresenter remoteBrowserPresenter;

        public PscpBrowserPanel()
        {
            InitializeComponent();
        }

        public PscpBrowserPanel(SessionData session, PscpOptions options) :
           // default value of localStartingDir moved to localPath in PscpBrowserPanel(SessionData session, PscpOptions options, string localStartingDir)            
           this(session, options, "")
        { }

        public PscpBrowserPanel(SessionData session, PscpOptions options, string localStartingDir)
            : this(session, options, localStartingDir, false)
        {
        }

        internal PscpBrowserPanel(SessionData session, PscpOptions options, string localStartingDir,
            bool deferInitialAuthentication) : this()
        {
            try
            {
                Session = session.CreateSnapshot();
                AssociateSavedSession(session);
                Name = TabText = Session.SessionName;
                string remotePath = String.IsNullOrEmpty(Session.RemotePath)
                    ? options.PscpHomePrefix + Session.Username
                    : Session.RemotePath;
                string localPath = String.IsNullOrEmpty(localStartingDir) ? Session.LocalPath : localStartingDir;
                if (String.IsNullOrEmpty(localPath)) localPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                fileTransferPresenter = new FileTransferPresenter(options);
                localBrowserPresenter = new BrowserPresenter("Local", new LocalBrowserModel(), Session, fileTransferPresenter);
                remoteBrowserPresenter = new BrowserPresenter("Remote", new RemoteBrowserModel(options), Session, fileTransferPresenter)
                {
                    DeferInitialAuthentication = deferInitialAuthentication
                };
                browserViewLocal.Initialize(localBrowserPresenter, new BrowserFileInfo(new DirectoryInfo(localPath)));
                browserViewRemote.Initialize(remoteBrowserPresenter, RemoteBrowserModel.NewDirectory(remotePath));
                fileTransferView.Initialize(fileTransferPresenter);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal Task<string> Startup { get { return remoteBrowserPresenter.FirstLoad; } }

        public SessionData Session { get; private set; }
    }
}
