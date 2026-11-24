using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;
using SuperPutty.Data;
using SuperPutty.Properties;
using SuperPutty.Utils;
using WeifenLuo.WinFormsUI.Docking;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Drawing;
using SuperPutty.Scp;
using SuperPuTTY.Scripting;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SuperPutty
{
    /// <summary>
    /// Represents the SuperPuTTY application itself
    /// </summary>
    public static class SuperPuTTY 
    {
        public const string DisplayName = "SuperPuTTY Community Edition";
        public const string ShortDisplayName = "SuperPuTTY CE";

        private static readonly ILog Log = LogManager.GetLogger(typeof(SuperPuTTY));

        public static event EventHandler<LayoutChangedEventArgs> LayoutChanging;
        public static event EventHandler<LayoutChangedEventArgs> LayoutChanged;

        public static event Action<string> StatusEvent;

        static BindingList<LayoutData> layouts = new BindingList<LayoutData>();
        static SortedList<string, SessionData> sessionsMap = new SortedList<string, SessionData>(StringComparer.OrdinalIgnoreCase);
        internal static bool IsUpdatingSessionCatalog { get; private set; }
        static SessionCatalogList sessionsList = new SessionCatalogList();
        private static ISessionStorage sessionStorage = new SessionStorage();
        private static string acceptedCatalogPath;
        private static bool sessionWriteInProgress;
        internal static bool HasPendingSessionMoves { get; private set; }
        internal static bool IsSessionWriteInProgress { get { return sessionWriteInProgress; } }
        private static long sessionCatalogVersion;
        internal static bool IsSessionCatalogReady { get; private set; } = true;
        static bool? isFirstRun;

        static SuperPuTTY()
        {
            sessionsList.ListChanged += delegate { sessionCatalogVersion++; };
        }

        internal sealed class SessionSaveResetEventArgs : ListChangedEventArgs
        {
            internal SessionSaveResetEventArgs() : base(ListChangedType.Reset, -1) { }
        }

        private sealed class SessionCatalogList : BindingList<SessionData>
        {
            internal void ResetAfterSave()
            {
                OnListChanged(new SessionSaveResetEventArgs());
            }
        }

        public static void Initialize(string[] args)
        {
            EnsureWritableSettingsFolder();
            Log.InfoFormat($"Initializing.  Version={Version}, UserSettings={Settings.SettingsFilePath}, SettingsFolder={Settings.SettingsFolder}");

            Images = LoadImageList("default", false);
            ImagesWithStop = LoadImageList("default", true);

            if (!IsFirstRun)
            {
                // parse command line args
                CommandLine = new CommandLineOptions(args);

                // display help if --help specified
                if (CommandLine.Help)
                {
                    if (DialogResult.Cancel == MessageBox.Show(CommandLineOptions.Usage(), "SuperPuTTY CE CLI Help", MessageBoxButtons.OKCancel))
                    {
                        Environment.Exit(0);
                    }
                }

                // load data                
                LoadLayouts();
                LoadInitialSessions();

                // determine starting layout, if any.  CLI has priority
                if (CommandLine.IsValid)
                {
                    if (CommandLine.Layout != null)
                    {
                        StartingLayout = FindLayout(CommandLine.Layout);
                        if (StartingLayout != null)
                        {
                            Log.InfoFormat($"Starting with layout from command line, {CommandLine.Layout}");
                        }
                    }
                    else
                    {
                        // ad-hoc session specified
                        SessionDataStartInfo sessionStartInfo = CommandLine.ToSessionStartInfo();
                        if (sessionStartInfo != null)
                        {
                            StartingSession = sessionStartInfo;
                            Log.InfoFormat($"Starting adhoc Session from command line, {StartingSession.Session.SessionId}");
                        }
                    }

                }

                // if nothing specified, then try the default layout
                if (StartingLayout == null && StartingSession == null)
                {
                    StartingLayout = FindLayout(Settings.DefaultLayoutName);
                    if (StartingLayout != null)
                    {
                        Log.InfoFormat($"Starting with default layout, {Settings.DefaultLayoutName}");
                    }
                }
            }

            // Program owns single-instance IPC after the main UI has been constructed.
            WindowEvents = new GlobalWindowEvents();

            Log.Info("Initialized");
        }

        private static void EnsureWritableSettingsFolder()
        {
            string configuredFolder = SettingsFolderResolver.GetConfiguredOrLegacyFolder(Settings.SettingsFolder);
            bool usedFallback;
            Exception configuredFolderError;
            string writableFolder = SettingsFolderResolver.ResolveWritableFolder(
                configuredFolder,
                out usedFallback,
                out configuredFolderError);

            if (usedFallback)
            {
                Log.WarnFormat(
                    "Configured settings folder is not writable; using Local AppData instead. folder={0}, fallback={1}, error={2}",
                    configuredFolder,
                    writableFolder,
                    configuredFolderError == null ? "unknown" : configuredFolderError.Message);
            }

            ApplyStartupSettingsFolder(writableFolder, usedFallback ? configuredFolder : null, Settings.Save);
        }

        internal static void ApplyStartupSettingsFolder(string writableFolder, string migrationSource, Action persist)
        {
            if (migrationSource != null)
            {
                try
                {
                    SettingsFolderResolver.CopyExistingSettings(migrationSource, writableFolder, path =>
                        ValidateSessionCatalog(new SessionStorage().LoadAsync(path, CancellationToken.None).GetAwaiter().GetResult()));
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException ||
                    ex is InvalidOperationException || ex is ArgumentException || ex is TimeoutException || ex is System.Security.SecurityException)
                {
                    throw new System.Configuration.ConfigurationErrorsException(
                        "SuperPuTTY could not move its settings to the fallback folder. The configured folder was kept. " +
                        "Check access to the original folder and its session catalog, then restart SuperPuTTY.\n\n" + ex.Message, ex);
                }
            }
            string original = Settings.SettingsFolder;
            if (String.Equals(original, writableFolder, StringComparison.OrdinalIgnoreCase)) return;
            Settings.SettingsFolder = writableFolder;
            try { persist(); }
            catch { Settings.SettingsFolder = original; throw; }
        }

        /// <summary>Called when application is shutting down, sends message to log.</summary>
        public static void Shutdown()
        {
            WindowEvents?.Dispose();
            WindowEvents = null;
            Log.Info("Shutting down...");
        }

        /// <summary>Send status message to toolstrip</summary>
        /// <param name="status">A string containing the message</param>
        /// <param name="args">optional arguments <seealso>
        ///         <cref>String.Format</cref>
        ///     </seealso>
        /// </param>
        public static void ReportStatus(string status, params object[] args)
        {
            string msg = args.Length > 0 ? string.Format(status, args) : status;
            Log.DebugFormat($"STATUS: {msg}");

            if (StatusEvent != null)
            {
                StatusEvent(msg);
            }
        }

        #region Layouts

        public static bool IsLayoutChanging { get; private set; }
        internal static bool IsLayoutTransitionInProgress { get; private set; }

        public static void AddLayout(string file)
        {
            LayoutData layout = new LayoutData(file);
            if (FindLayout(layout.Name) == null)
            {
                layouts.Add(layout);
                LoadLayout(layout, true);
            }
        }

        public static void RemoveLayout(string name, bool deleteFile)
        {
            LayoutData layout = FindLayout(name);
            if (layout != null)
            {
                if (deleteFile)
                {
                    LayoutStorage.DeleteFiles(layout.FilePath);
                }
                if (CurrentLayout != null && (ReferenceEquals(CurrentLayout, layout) ||
                    SettingsFolderResolver.PathsEqual(CurrentLayout.FilePath, layout.FilePath)))
                    DetachCurrentLayout();
                layouts.Remove(layout);
            }
        }

        public static void RenameLayout(LayoutData layout, string newName)
        {
            RenameLayout(layout, newName, () => Settings.Save());
        }

        internal static void RenameLayout(LayoutData layout, string newName, Action persistSettings)
        {
            if (layout == null) return;
            string error;
            if (layout.IsReadOnly) throw new ArgumentException("This layout cannot be renamed.");
            if (!LayoutData.TryValidateName(newName, out error)) throw new ArgumentException(error, nameof(newName));
            if (string.Equals(layout.Name, newName, StringComparison.OrdinalIgnoreCase)) return;
            if (layouts.Any(item => item != layout && string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A layout with the same name exists.", nameof(newName));

            string fileOld = Path.GetFullPath(layout.FilePath);
            string directory = Path.GetDirectoryName(fileOld);
            string fileNew = Path.GetFullPath(Path.Combine(directory, newName + ".xml"));
            if (!string.Equals(Path.GetDirectoryName(fileNew), directory, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The renamed layout must stay in its original directory.", nameof(newName));

            string oldDefault = Settings.DefaultLayoutName;
            bool wasDefault = string.Equals(oldDefault, layout.Name, StringComparison.OrdinalIgnoreCase);
            LayoutStorage.MoveFiles(fileOld, fileNew);
            if (wasDefault)
            {
                try
                {
                    Settings.DefaultLayoutName = newName;
                    persistSettings();
                }
                catch
                {
                    Settings.DefaultLayoutName = oldDefault;
                    LayoutStorage.MoveFiles(fileNew, fileOld);
                    throw;
                }
            }
            layout.Name = newName;
            layout.FilePath = fileNew;
            int index = layouts.IndexOf(layout);
            if (index >= 0) layouts.ResetItem(index);
        }

        public static LayoutData FindLayout(string name)
        {
            return layouts.FirstOrDefault(layout => name == layout.Name);
        }

        public static void LoadLayouts()
        {
            if (!String.IsNullOrEmpty(Settings.SettingsFolder))
            {
                layouts.Clear();
                LayoutData autoRestore = new LayoutData(AutoRestoreLayoutPath)
                {
                    Name = LayoutData.AutoRestore,
                    IsReadOnly = true
                };
                layouts.Add(autoRestore);
                if (Directory.Exists(LayoutsDir))
                {
                    List<LayoutData> newLayouts = LayoutStorage.Discover(LayoutsDir).Select(file => new LayoutData(file)).ToList();

                    foreach (LayoutData layout in newLayouts)
                    {
                        layouts.Add(layout);
                    }
                    Log.InfoFormat($"Loaded {newLayouts.Count} layouts");
                }
                else
                {
                    Log.InfoFormat($"Creating layouts directory: {LayoutsDir}");
                    Directory.CreateDirectory(LayoutsDir);
                }
            }
        }

        internal static void DetachCurrentLayout()
        {
            // The visible workspace stays intact, but its former file is no longer
            // the save destination after changing profiles.
            CurrentLayout = null;
            MainForm?.ClearLayoutStatus();
        }

        public static void LoadLayout(LayoutData layout)
        {
            LoadLayout(layout, false);
        }

        public static void LoadLayout(LayoutData layout, bool isNewLayoutAlreadyActive)
        {
            if (IsLayoutTransitionInProgress)
            {
                Log.Debug("Ignoring a reentrant layout load. The current transition must finish first.");
                return;
            }
            Log.InfoFormat("LoadLayout: layout={0}, isNewLayoutAlreadyActive={1}", layout == null ? "NULL" : layout.Name, isNewLayoutAlreadyActive);
            LayoutChangedEventArgs args = new LayoutChangedEventArgs
            {
                New = layout,
                Old = CurrentLayout,
                IsNewLayoutAlreadyActive = isNewLayoutAlreadyActive
            };

            IsLayoutTransitionInProgress = true;
            try
            {
                IsLayoutChanging = true;
                try { LayoutChanging?.Invoke(typeof(SuperPuTTY), args); }
                finally { IsLayoutChanging = false; }
                if (!args.Cancel)
                {
                    CurrentLayout = args.New;
                    LayoutChanged?.Invoke(typeof(SuperPuTTY), args);
                }
                // Modal notices pump the message loop. Hold forwarded commands
                // until both the save target and the visible workspace are settled.
                args.CompletionNotice?.Invoke();
            }
            finally
            {
                IsLayoutTransitionInProgress = false;
                SingleInstanceHelper.ResumePendingCommands();
            }
        }

        internal static string SaveLayoutFile(string path, Action<string> serialize, Action<byte[]> validateLayout)
        {
            if (IsLayoutTransitionInProgress)
                throw new InvalidOperationException("Wait for the layout change to finish before saving it.");
            string catalogPath = String.IsNullOrWhiteSpace(Settings.SettingsFolder) ? null : SessionsFileName;
            string settingsPath = Settings.SettingsFilePath;
            var protectedPaths = new[]
            {
                catalogPath, acceptedCatalogPath, settingsPath, settingsPath == null ? null : settingsPath + ".bak"
            }.Where(item => !String.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return LayoutStorage.SaveAtomically(path, serialize, validateLayout, destination =>
            {
                foreach (string dataFile in protectedPaths)
                {
                    if (SharedFile.IsSameDestination(dataFile, destination))
                        throw new IOException("Choose a different layout filename. This path is reserved for application data.");
                }
            });
        }

        public static void LoadLayoutInNewInstance(LayoutData layout)
        {
            ReportStatus($"Starting new instance with layout, {layout.Name}");
            Process.Start(Assembly.GetExecutingAssembly().Location, BuildNewInstanceArguments("-layout", layout.Name));
        }

        public static void LoadSessionInNewInstance(string sessionId)
        {
            ReportStatus($"Starting session in new instance, {sessionId}");
            Process.Start(Assembly.GetExecutingAssembly().Location, BuildNewInstanceArguments("-session", sessionId));
        }

        internal static string BuildNewInstanceArguments(string option, string value)
        {
            return option + " " + CommandLineOptions.QuoteArgument(value);
        }

        public static void SetLayoutAsDefault(string layoutName)
        {
            if (!string.IsNullOrEmpty(layoutName))
            {
                LayoutData layout = FindLayout(layoutName);
                if (layout != null)
                {
                    ReportStatus($"Setting {layoutName} as default layout");
                    string previousDefault = Settings.DefaultLayoutName;
                    Settings.DefaultLayoutName = layoutName;
                    try { Settings.Save(); }
                    catch
                    {
                        Settings.DefaultLayoutName = previousDefault;
                        throw;
                    }

                    // so gui change is propagated via events
                    LoadLayouts();
                }
            }
        }

        #endregion

        #region Sessions

        /// <summary>Returns A string containing the path to the saved sessions database on disk</summary>
        private static string SessionsFileName { get { return Path.Combine(Settings.SettingsFolder, "Sessions.XML"); } }

        internal static void LoadInitialSessions()
        {
            IsSessionCatalogReady = false;
            LoadSessions();
        }

        private static bool SessionCatalogFileExists(string fileName)
        {
            // Treat only genuinely missing paths as a new catalog. File.Exists also
            // reports false for access errors and directories at the expected path.
            try { using (SharedFile.OpenRead(fileName)) { } return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        /// <summary>Load sessions database from file into the application</summary>
        public static void LoadSessions()
        {
            string fileName = SessionsFileName;
            Log.InfoFormat($"Loading all sessions.  file={fileName}");

            try
            {
                EnsureCatalogCanBeReplaced(false);
                if (SessionCatalogFileExists(fileName))
                {
                    var loadingStorage = new SessionStorage();
                    ReplaceSessionCatalog(loadingStorage.LoadAsync(fileName, CancellationToken.None).GetAwaiter().GetResult());
                    sessionStorage = loadingStorage;
                    acceptedCatalogPath = Path.GetFullPath(fileName);
                    IsSessionCatalogReady = true;
                }
                else
                {
                    Log.WarnFormat($"Sessions file does not exist, nothing loaded.  file={fileName}");
                    (sessionStorage as SessionStorage)?.RememberMissing(fileName);
                    acceptedCatalogPath = Path.GetFullPath(fileName);
                    IsSessionCatalogReady = true;
                }

            }
            catch (Exception ex)
            {
                Log.Error($"Error while loading sessions from {fileName}", ex);
            }
        }

        internal static async Task LoadSessionsAsync(CancellationToken cancellation, ISessionStorage storage = null,
            bool discardPendingMoves = false)
        {
            EnsureCatalogCanBeReplaced(discardPendingMoves);
            cancellation.ThrowIfCancellationRequested();
            string fileName = SessionsFileName;
            long version = sessionCatalogVersion;
            var loadingStorage = storage ?? new SessionStorage();
            var replacement = storage == null
                ? await ((SessionStorage)loadingStorage).LoadPrimaryOrMissingAsync(fileName, cancellation)
                : await loadingStorage.LoadAsync(fileName, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (version != sessionCatalogVersion || !String.Equals(fileName, SessionsFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Sessions changed while loading. Reload again to replace the current catalog.");
            EnsureCatalogCanBeReplaced(discardPendingMoves);
            ReplaceSessionCatalog(replacement ?? new List<SessionData>());
            if (storage == null) sessionStorage = loadingStorage;
            acceptedCatalogPath = Path.GetFullPath(fileName);
            IsSessionCatalogReady = true;
            HasPendingSessionMoves = false;
        }

        internal sealed class PreparedCatalog
        {
            internal string Path;
            internal ISessionStorage Storage;
            internal List<SessionData> Sessions;
            internal long Version;
            internal bool Ready;
            internal bool PendingMoves;
            internal bool DiscardPendingMoves;
        }

        internal static PreparedCatalog CaptureCatalog()
        {
            return new PreparedCatalog { Path = acceptedCatalogPath, Storage = sessionStorage,
                Sessions = GetAllSessions(), Version = sessionCatalogVersion, Ready = IsSessionCatalogReady,
                PendingMoves = HasPendingSessionMoves };
        }

        internal static bool HasAcceptedCatalog(string folder)
        {
            return IsSessionCatalogReady && String.Equals(acceptedCatalogPath,
                Path.GetFullPath(Path.Combine(folder, "Sessions.XML")), StringComparison.OrdinalIgnoreCase);
        }

        internal static async Task<PreparedCatalog> PrepareCatalogAsync(string folder, CancellationToken cancellation,
            bool discardPendingMoves = false)
        {
            EnsureCatalogCanBeReplaced(discardPendingMoves);
            string path = Path.GetFullPath(Path.Combine(folder, "Sessions.XML"));
            string originalPath = acceptedCatalogPath;
            var storage = new SessionStorage();
            var prepared = new PreparedCatalog { Path = path, Storage = storage,
                Sessions = GetAllSessions(), Version = sessionCatalogVersion, Ready = true, DiscardPendingMoves = discardPendingMoves };
            var loaded = await storage.LoadPrimaryOrMissingAsync(path, cancellation);
            if (loaded != null) prepared.Sessions = loaded;
            else if (discardPendingMoves && HasPendingSessionMoves)
                prepared.Sessions = (originalPath == null ? null :
                    await new SessionStorage().LoadPrimaryOrMissingAsync(originalPath, cancellation)) ?? new List<SessionData>();
            cancellation.ThrowIfCancellationRequested();
            ValidateSessionCatalog(prepared.Sessions);
            return prepared;
        }

        internal static void AcceptCatalog(PreparedCatalog prepared, bool restore = false)
        {
            if (!restore)
            {
                EnsureCatalogCanBeReplaced(prepared.DiscardPendingMoves);
                if (prepared.Version != sessionCatalogVersion)
                    throw new InvalidOperationException("Sessions changed while loading the settings folder. Try saving Options again.");
            }
            ReplaceSessionCatalog(prepared.Sessions);
            sessionStorage = prepared.Storage;
            acceptedCatalogPath = prepared.Path;
            IsSessionCatalogReady = prepared.Ready;
            HasPendingSessionMoves = prepared.PendingMoves;
        }

        private static void EnsureCatalogCanBeReplaced(bool discardPendingMoves)
        {
            EnsureSessionCatalogIdle();
            if (HasPendingSessionMoves && !discardPendingMoves)
                throw new InvalidOperationException("Save or discard the pending session moves before replacing the catalog.");
        }

        internal static async Task<bool?> ResolvePendingSessionMovesAsync(IWin32Window owner)
        {
            EnsureSessionCatalogIdle();
            if (!HasPendingSessionMoves) return false;

            MainForm?.PauseDelayedSessionSave();
            var result = MessageBox.Show(owner, "There are unsaved session moves.\n\n" +
                "Yes saves the moves, No discards them, and Cancel keeps the current catalog.",
                "Unsaved Session Moves", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.Cancel)
            {
                ReportStatus("The session moves are still unsaved.");
                return null;
            }
            if (result == DialogResult.No) return true;
            await SaveSessionsAsync();
            return false;
        }

        private static SortedList<string, SessionData> ValidateSessionCatalog(
            IEnumerable<SessionData> sessions, IDictionary<SessionData, string> renamed = null)
        {
            var staged = new SortedList<string, SessionData>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in sessions)
            {
                string id = session == null ? null : renamed != null && renamed.ContainsKey(session)
                    ? renamed[session] : session.SessionId;
                if (session == null || String.IsNullOrWhiteSpace(id) || String.IsNullOrWhiteSpace(session.SessionName) ||
                    id.Split('/').Any(String.IsNullOrWhiteSpace))
                    throw new InvalidDataException("Every session requires a valid ID and name.");
                if (!String.Equals(SessionData.GetSessionNameFromId(id), session.SessionName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The session name must match the last part of its ID: " + id);
                if (staged.ContainsKey(id))
                    throw new InvalidDataException("Conflicting session IDs (case is ignored): " + id);
                staged.Add(id, session);
            }
            foreach (string id in staged.Keys)
            {
                string parent = SessionData.GetSessionParentId(id);
                while (parent != null)
                {
                    if (staged.ContainsKey(parent))
                        throw new InvalidDataException("A session cannot also be a folder: " + parent);
                    parent = SessionData.GetSessionParentId(parent);
                }
            }
            return staged;
        }

        internal static void ReplaceSessionCatalog(IEnumerable<SessionData> sessions)
        {
            EnsureSessionCatalogIdle();
            var replacement = sessions.ToList();
            var staged = ValidateSessionCatalog(replacement);
            bool notify = sessionsList.RaiseListChangedEvents;
            try
            {
                sessionsList.RaiseListChangedEvents = false;
                sessionsMap = staged;
                sessionsList.Clear();
                foreach (var session in replacement) sessionsList.Add(session);
            }
            finally { sessionsList.RaiseListChangedEvents = notify; }
            HasPendingSessionMoves = false;
            if (notify) sessionsList.ResetBindings();
        }

        internal static bool SessionPathExists(string id)
        {
            return sessionsMap.ContainsKey(id) || sessionsMap.Keys.Any(key =>
                key.StartsWith(id + "/", StringComparison.OrdinalIgnoreCase));
        }

        internal static bool SessionPathConflicts(string id, SessionData except = null)
        {
            return sessionsMap.Any(pair => !Object.ReferenceEquals(pair.Value, except) &&
                (String.Equals(pair.Key, id, StringComparison.OrdinalIgnoreCase) ||
                 pair.Key.StartsWith(id + "/", StringComparison.OrdinalIgnoreCase) ||
                 id.StartsWith(pair.Key + "/", StringComparison.OrdinalIgnoreCase)));
        }

        internal static void SaveSessionChanges(SessionData session, SessionData edited, Action persistSessions)
        {
            SaveSessionChangesCoreAsync(session, edited, AsCompletedTask(persistSessions)).GetAwaiter().GetResult();
        }

        internal static Task SaveSessionChangesAsync(SessionData session, SessionData edited)
        {
            return SaveSessionChangesCoreAsync(session, edited, SaveSessionsAsync);
        }

        private static Func<Task> AsCompletedTask(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return () => { action(); return Task.CompletedTask; };
        }

        private static async Task SaveSessionChangesCoreAsync(SessionData session, SessionData edited, Func<Task> persistSessions)
        {
            EnsureSessionCatalogReady();
            if (session == null || edited == null || persistSessions == null)
                throw new ArgumentNullException(session == null ? nameof(session) : edited == null ? nameof(edited) : nameof(persistSessions));

            bool saved = sessionsList.Contains(session);
            var replacement = sessionsList.Where(s => !Object.ReferenceEquals(s, session)).Concat(new[] { edited });
            ValidateSessionCatalog(replacement);
            var original = (SessionData)session.Clone();
            var originalMap = sessionsMap;
            bool notify = sessionsList.RaiseListChangedEvents;
            bool updating = IsUpdatingSessionCatalog;
            try
            {
                IsUpdatingSessionCatalog = true;
                sessionsList.RaiseListChangedEvents = false;
                session.CopyFrom(edited);
                if (!String.Equals(session.SessionId, edited.SessionId, StringComparison.Ordinal) ||
                    !String.Equals(session.SessionName, edited.SessionName, StringComparison.Ordinal))
                    throw new InvalidOperationException("A session name or ID change was canceled.");
                if (!saved) sessionsList.Add(session);
                sessionsMap = ValidateSessionCatalog(sessionsList);
                await persistSessions();
            }
            catch
            {
                session.CopyFrom(original);
                if (!saved) sessionsList.Remove(session);
                sessionsMap = originalMap;
                throw;
            }
            finally
            {
                IsUpdatingSessionCatalog = updating;
                sessionsList.RaiseListChangedEvents = notify;
                if (notify) sessionsList.ResetAfterSave();
            }
        }

        internal static List<SessionData> GetSessionsInFolder(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder)) throw new ArgumentException("A folder path is required.");
            return GetAllSessions().Where(session => session.SessionId.StartsWith(folder + "/",
                StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static void ChangeSessionIds(IDictionary<SessionData, string> renamed)
        {
            EnsureSessionCatalogIdle();
            var staged = ValidateSessionCatalog(sessionsList, renamed);
            var original = renamed.Keys.ToDictionary(session => session, session => session.SessionId);
            IsUpdatingSessionCatalog = true;
            try
            {
                foreach (var pair in renamed)
                {
                    pair.Key.SessionId = pair.Value;
                    if (!String.Equals(pair.Key.SessionId, pair.Value, StringComparison.Ordinal))
                        throw new InvalidOperationException("A session ID change was canceled.");
                }
                sessionsMap = staged;
                HasPendingSessionMoves = true;
            }
            catch
            {
                foreach (var pair in original) pair.Key.SessionId = pair.Value;
                throw;
            }
            finally { IsUpdatingSessionCatalog = false; }
            sessionsList.ResetBindings();
        }

        internal static int MoveSessionFolder(string oldPath, string newPath)
        {
            if (String.IsNullOrWhiteSpace(oldPath) || String.IsNullOrWhiteSpace(newPath))
                throw new ArgumentException("Folder paths cannot be empty.");
            bool same = String.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
            if (!same && (newPath.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase) ||
                oldPath.StartsWith(newPath + "/", StringComparison.OrdinalIgnoreCase) || SessionPathExists(newPath)))
                throw new InvalidOperationException("The destination folder already exists or overlaps the source.");
            var affected = GetSessionsInFolder(oldPath);
            if (affected.Count > 0)
                ChangeSessionIds(affected.ToDictionary(session => session,
                    session => newPath + session.SessionId.Substring(oldPath.Length)));
            return affected.Count;
        }

        internal static void MoveSavedSession(SessionData session, string newId)
        {
            if (!Object.ReferenceEquals(GetSessionById(session.SessionId), session))
                throw new InvalidOperationException("The session is not in the catalog.");
            ChangeSessionIds(new Dictionary<SessionData, string> { { session, newId } });
        }

        internal static void RemoveSessionFolder(string folder)
        {
            var affected = GetSessionsInFolder(folder);
            bool notify = sessionsList.RaiseListChangedEvents;
            try
            {
                sessionsList.RaiseListChangedEvents = false;
                foreach (var session in affected) RemoveSession(session.SessionId);
            }
            finally { sessionsList.RaiseListChangedEvents = notify; }
            if (notify) sessionsList.ResetBindings();
        }

        internal static void DeleteSessions(IEnumerable<SessionData> sessions, Action persistSessions)
        {
            DeleteSessionsCoreAsync(sessions, AsCompletedTask(persistSessions)).GetAwaiter().GetResult();
        }

        internal static Task DeleteSessionsAsync(IEnumerable<SessionData> sessions)
        {
            return DeleteSessionsCoreAsync(sessions, SaveSessionsAsync);
        }

        private static async Task DeleteSessionsCoreAsync(IEnumerable<SessionData> sessions, Func<Task> persistSessions)
        {
            EnsureSessionCatalogReady();
            if (sessions == null || persistSessions == null)
                throw new ArgumentNullException(sessions == null ? nameof(sessions) : nameof(persistSessions));
            var removed = new HashSet<SessionData>(sessions);
            var original = GetAllSessions();
            var existing = new HashSet<SessionData>(original);
            if (!removed.IsSubsetOf(existing))
                throw new InvalidOperationException("A session selected for deletion is no longer in the catalog.");
            var remaining = original.Where(session => !removed.Contains(session)).ToList();
            var originalMap = sessionsMap;
            bool notify = sessionsList.RaiseListChangedEvents;
            bool updating = IsUpdatingSessionCatalog;
            try
            {
                IsUpdatingSessionCatalog = true;
                sessionsList.RaiseListChangedEvents = false;
                sessionsMap = ValidateSessionCatalog(remaining);
                sessionsList.Clear();
                foreach (var session in remaining) sessionsList.Add(session);
                await persistSessions();
            }
            catch
            {
                sessionsMap = originalMap;
                sessionsList.Clear();
                foreach (var session in original) sessionsList.Add(session);
                throw;
            }
            finally
            {
                IsUpdatingSessionCatalog = updating;
                sessionsList.RaiseListChangedEvents = notify;
                if (notify) sessionsList.ResetAfterSave();
            }
        }

        /// <summary>Save in-application Session Database to XML File</summary>
        public static void SaveSessions()
        {
            string path = GetAcceptedSessionSavePath();
            Log.InfoFormat("Saving all sessions");
            sessionStorage.Save(GetAllSessions(), path);
            MarkSessionMovesSaved();
        }

        private static string GetAcceptedSessionSavePath()
        {
            EnsureSessionCatalogReady();
            string path = Path.GetFullPath(SessionsFileName);
            if (!String.Equals(acceptedCatalogPath, path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The settings folder changed. Load its session catalog successfully before saving.");
            return path;
        }

        internal static async Task SaveSessionsAsync()
        {
            string path = GetAcceptedSessionSavePath();
            var storage = sessionStorage;
            var snapshot = CloneSessions();
            sessionWriteInProgress = true;
            try
            {
                await Task.Run(() => storage.Save(snapshot, path));
                MarkSessionMovesSaved();
            }
            finally { sessionWriteInProgress = false; }
        }

        internal static async Task ExportSessionsAsync(string destination)
        {
            EnsureSessionCatalogIdle();
            string configuredFolder = Settings.SettingsFolder;
            string active = String.IsNullOrWhiteSpace(configuredFolder) ? null : SessionsFileName;
            string accepted = acceptedCatalogPath;
            var snapshot = CloneSessions();
            sessionWriteInProgress = true;
            try
            {
                await Task.Run(() =>
                {
                    if ((active != null && SharedFile.IsSameDestination(active, destination)) ||
                        (accepted != null && !String.Equals(active, accepted, StringComparison.OrdinalIgnoreCase) &&
                            SharedFile.IsSameDestination(accepted, destination)))
                        throw new InvalidOperationException("Choose a different file. Export cannot replace the active session catalog.");
                    new SessionStorage().Save(snapshot, destination);
                });
            }
            finally { sessionWriteInProgress = false; }
        }

        private static List<SessionData> CloneSessions()
        {
            return sessionsList.Select(session => session.SnapshotForPersistence()).ToList();
        }

        private static void MarkSessionMovesSaved()
        {
            if (!HasPendingSessionMoves) return;
            HasPendingSessionMoves = false;
            sessionCatalogVersion++;
        }

        private static void EnsureSessionCatalogIdle()
        {
            if (sessionWriteInProgress)
                throw new InvalidOperationException("A session save is already in progress. Wait for it to finish and try again.");
        }

        private static void EnsureSessionCatalogReady()
        {
            EnsureSessionCatalogIdle();
            if (!IsSessionCatalogReady)
                throw new InvalidOperationException("Saved sessions could not be loaded. Reload Sessions successfully before saving or importing changes.");
        }

        /// <summary>
        /// Remove a session from the in-application sessions database. 
        /// </summary>
        /// <param name="sessionId">The <seealso cref="SessionData.SessionId"/> of the session to remove</param>
        /// <returns>true on success, or false on failure or if session did not exist</returns>
        public static bool RemoveSession(string sessionId)
        {
            EnsureSessionCatalogIdle();
            SessionData session = GetSessionById(sessionId);
            if (session != null)
            {
                sessionsMap.Remove(sessionId);
                sessionsList.Remove(session);
                Log.InfoFormat($"Removed Session, id={sessionId}, success=true");
                return true;
            }            
            return false;
        }

        /// <summary>Get a Session by its <seealso cref="SessionData.SessionId"/></summary>
        /// <param name="sessionId">A string which represents a session</param>
        /// <returns>A <seealso cref="SessionData"/> object containing the session details</returns>
        public static SessionData GetSessionById(string sessionId)
        {
            SessionData session = null;
            if (sessionId != null)
            {
                if (!sessionsMap.TryGetValue(sessionId, out session))
                {
                    // no hit by id...so try the list
                    // @TODO: Revisit...this is a work around the sessionId changing in tree
                    foreach (SessionData sd in sessionsList)
                    {
                        if (String.Equals(sd.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                        {
                            session = sd;
                            // reindex list
                            sessionsMap = ValidateSessionCatalog(sessionsList);
                            break;
                        }
                    }
                }
            }
            return session;
        }

        /// <summary>Add a new session to the in-application session database</summary>
        /// <param name="session">A <seealso cref="SessionData"/> object containing the configuration of a session</param>
        /// <returns>true on success, false on failure</returns>
        public static bool AddSession(SessionData session)
        {
            EnsureSessionCatalogIdle();
            if (session == null || String.IsNullOrWhiteSpace(session.SessionId) || String.IsNullOrWhiteSpace(session.SessionName) ||
                session.SessionId.Split('/').Any(String.IsNullOrWhiteSpace))
                throw new ArgumentException("A session requires a valid ID and name.");
            bool success = false;
            bool conflict = GetSessionById(session.SessionId) != null || SessionPathExists(session.SessionId);
            for (string parent = SessionData.GetSessionParentId(session.SessionId); !conflict && parent != null;
                parent = SessionData.GetSessionParentId(parent))
                conflict = sessionsMap.ContainsKey(parent);
            if (!conflict)
            {
                Log.InfoFormat($"Added Session, id={session.SessionId}");
                sessionsMap.Add(session.SessionId, session);
                sessionsList.Add(session);
                success = true;
            }
            else
            {
                Log.InfoFormat($"Failed to Add Session, id={session.SessionId}.  Session already exists");
            }
            return success;
        }

        /// <summary>Get a list of all sessions from the in-application database</summary>
        /// <returns>A List of <seealso cref="SessionData"/> objects</returns>
        public static List<SessionData> GetAllSessions()
        {
            return sessionsMap.Values.ToList();
        }

        /// <summary>Retrieve a <seealso cref="SessionData"/> object and open a new putty window</summary>
        /// <param name="sessionId">A string containing the <seealso cref="SessionData.SessionId"/> of the session</param>
        public static void OpenProtoSession(string sessionId)
        {
            OpenProtoSession(GetSessionById(sessionId));
        }

        internal const int MaximumOpenSessions = 200;

        internal static bool HasSessionCapacity(DockPanel panel)
        {
            return panel != null && !panel.IsDisposed && panel.Contents.OfType<ToolWindowDocument>()
                .Count(content => !content.IsDisposed) < MaximumOpenSessions;
        }

        private static bool CanOpenAnotherSession()
        {
            if (MainForm != null && HasSessionCapacity(MainForm.DockPanel)) return true;
            ReportStatus("The maximum of {0} open session tabs has been reached. Close a tab before opening another.", MaximumOpenSessions);
            return false;
        }

        /// <summary>Open a new putty window with its settings being passed in a <seealso cref="SessionData"/> object</summary>
        /// <param name="session">The <seealso cref="SessionData"/> object containing the settings</param>
        public static ctlPuttyPanel OpenProtoSession(SessionData session)
        {
            if (IsScpSession(session))
            {
                OpenScpSession(session);
                return null;
            }

            if (session != null && !CanOpenAnotherSession()) return null;

            Log.InfoFormat("Opening putty session, id={0}", session == null ? "" : session.SessionId);
            ctlPuttyPanel panel = null;
            if (session != null)
            {
                string Executable = PuttyStartInfo.GetExecutable(session);
                if (string.IsNullOrEmpty(Executable))
                {
                    MessageBox.Show("Error trying to create session: " + session.ToString() +
                        "\nExecutable not set for " + session.Proto.ToString() + " protocol." +
                        "\nGo to tools->options->General tab to set the path to the executable."
                        , "Failed to create a session", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }
                if (!File.Exists(Executable))
                {
                    MessageBox.Show("Error trying to create session: " + session.ToString() +
                        "\nExecutable not found for " + session.Proto.ToString() + " protocol." +
                        "\nThe path for the executable was set as \"" + Executable + "\"." +
                        "\nGo to tools->options->General tab to set the path to the executable."
                        , "Failed to create a session", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }

                // This is the callback fired when the panel containing the terminal is closed
                // We use this to save the last docking location and to close the panel
                PuttyClosedCallback callback = delegate
                {
                    _ = CloseExitedSessionAsync(panel, session, SaveSessionsAsync);
                };

                try {
                    panel = new ctlPuttyPanel(session, callback);

                    ApplyDockRestrictions(panel);
                    ApplyIconForWindow(panel, session);
                    panel.Show(MainForm.DockPanel, session.LastDockstate);
                    ReportStatus($"Opened session: {session.SessionId} [{session.Proto}]");

                    if (!string.IsNullOrWhiteSpace(session.SPSLFileName))
                        StartSessionScript(panel, session.SPSLFileName);
                } catch (InvalidOperationException ex)
                {
                    MessageBox.Show("Error trying to create session " + ex.Message, "Failed to create session panel", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            return panel;
        }

        internal static void CloseExitedSession(ctlPuttyPanel panel, SessionData session, Action persist)
        {
            CloseExitedSessionAsync(panel, session, AsCompletedTask(persist)).GetAwaiter().GetResult();
        }

        private static async Task CloseExitedSessionAsync(ctlPuttyPanel panel, SessionData session, Func<Task> persist)
        {
            if (panel == null || panel.IsDisposed || panel.Disposing || !panel.IsHandleCreated) return;
            if (panel.InvokeRequired)
            {
                try { panel.BeginInvoke((Action)(() => { _ = CloseExitedSessionAsync(panel, session, persist); })); }
                catch (InvalidOperationException) { } // The tab closed before the post.
                return;
            }
            try
            {
                if (session.LastDockstate != panel.DockState && panel.DockState != DockState.Unknown &&
                    panel.DockState != DockState.Hidden)
                {
                    var previous = session.LastDockstate;
                    session.LastDockstate = panel.DockState;
                    // A pending move is written only by its save or the next
                    // explicit catalog edit, never as a tab-close side effect.
                    if (HasPendingSessionMoves) return;
                    try { await persist(); }
                    catch
                    {
                        session.LastDockstate = previous;
                        throw;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException ||
                ex is System.Security.SecurityException || ex is InvalidOperationException)
            {
                Log.Warn("Unable to save the exited session's docking position.", ex);
                ReportStatus("The session closed, but its docking position could not be saved.");
            }
            finally { if (!panel.IsDisposed && !panel.Disposing) panel.CloseWithoutConfirmation(); }
        }

        private static async void StartSessionScript(ctlPuttyPanel panel, string fileName)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                EventHandler closed = (sender, args) => cancellation.Cancel();
                panel.Disposed += closed;
                try
                {
                    string script = string.Empty;

                    if(Regex.IsMatch(fileName, @"^https?:\/\/", RegexOptions.IgnoreCase))
                    {
                        Uri scriptUri;
                        if (!RemoteSpslLoader.TryGetSecureUri(fileName, out scriptUri))
                        {
                            MessageBox.Show(
                                "Remote SPSL scripts must use HTTPS. The script was not executed.\n\n" + fileName,
                                "Blocked Insecure Remote Script",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }
                        else if (MessageBox.Show(
                            "This remote SPSL script can type commands into the session.\n\n" +
                            scriptUri.AbsoluteUri +
                            "\n\nDownload and run this script?",
                            "Trust Remote SPSL Script?",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                        {
                            try
                            {
                                script = await RemoteSpslLoader.DownloadAsync(scriptUri, cancellation.Token);
                            }
                            catch (OperationCanceledException) { return; }
                            catch(Exception ex)
                            {
                                Log.Warn("Unable to securely download remote SPSL script from host " + scriptUri.Host, ex);
                                if (panel.IsDisposed || panel.Disposing) return;
                                MessageBox.Show(
                                    "The remote SPSL script could not be downloaded securely and was not executed.",
                                    "Remote Script Error",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning);
                            }
                        }
                    }
                    else
                    {
                        if (fileName.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                        {
                            fileName = fileName.Substring("file://".Length);
                        }

                        if (File.Exists(fileName))
                        {
                            script = await Task.Run(() => File.ReadAllText(fileName), cancellation.Token);
                        }
                    }

                    if (panel.IsDisposed || panel.Disposing || cancellation.IsCancellationRequested) return;
                    if (!String.IsNullOrEmpty(script))
                    {
                        panel.AppPanel.WhenCaptured(() =>
                        {
                            ExecuteScriptEventArgs scriptArgs = new ExecuteScriptEventArgs() { Script = script, Targets = new[] { SPSL.ScriptTarget.ForPanel(panel.AppPanel) } };
                            SPSL.BeginExecuteScript(scriptArgs);
                        });
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Log.Warn("Unable to load startup script", ex);
                    if (!panel.IsDisposed && !panel.Disposing)
                        ReportStatus("Startup script could not be loaded.");
                }
                finally { panel.Disposed -= closed; }
            }
        }

        /// <summary>Retrieve a <seealso cref="SessionData"/> object and open a new putty scp window</summary>
        /// <param name="sessionId">A string containing the <seealso cref="SessionData.SessionId"/> of the session</param>
        public static void OpenScpSession(string sessionId)
        {
            OpenScpSession(GetSessionById(sessionId));
        }

        /// <summary>Open a new putty scp window with its settings being passed in a <seealso cref="SessionData"/> object</summary>
        /// <param name="session">The <seealso cref="SessionData"/> object containing the settings</param>
        public static void OpenScpSession(SessionData session)
        {
            if (session != null && !CanOpenAnotherSession()) return;
            Log.InfoFormat("Opening scp session, id={0}", session == null ? "" : session.SessionId);
            if (session == null)
            {
                Log.Warn("Could not open null session");
            }
            else if (!IsScpEnabled)
            {
                ReportStatus($"Could not open session, pscp not found: {session.SessionId} [SCP]");
            }
            else
            {
                var homePrefix = String.Equals(session.Username, "root", StringComparison.OrdinalIgnoreCase)
                    ? Settings.PscpRootHomePrefix
                    : Settings.PscpHomePrefix;
                PscpBrowserPanel panel = new PscpBrowserPanel(
                    session, new PscpOptions { PscpLocation = Settings.PscpExe, PscpHomePrefix = homePrefix });
                ApplyDockRestrictions(panel);
                ApplyIconForWindow(panel, session);
                panel.Show(MainForm.DockPanel, session.LastDockstate);
                ReportStatus($"Opened session: {session.SessionId} [SCP]");
            }
        }

        /// <summary>Apply docking restrictions to a <seealso cref="ToolWindowDocument"/> window such as preventing a window opening from outside the tabbed interface</summary>
        /// <param name="panel">The <seealso cref="DockPanel"/> to apply the restrictions to</param>
        public static void ApplyDockRestrictions(DockPanel panel)
        {
            foreach (DockContent doc in panel.Documents)
            {
                if (doc is ToolWindowDocument)
                {
                    ApplyDockRestrictions(doc);
                }
            }
        }

        /// <summary>Apply docking restrictions to a panel, such as restricting a panel from floating</summary>
        /// <param name="panel">The <seealso cref="DockContent"/> panel to apply the restrictions to</param>
        public static void ApplyDockRestrictions(DockContent panel)
        {
            DockAreas areas = originalDockAreas.GetValue(panel, p => new DockingDefaults(p.DockAreas)).Areas;
            if (Settings.RestrictContentToDocumentTabs) areas &= DockAreas.Document | DockAreas.Float;
            if (Settings.DockingRestrictFloatingWindows) areas &= ~DockAreas.Float;
            panel.DockAreas = areas;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DockContent, DockingDefaults>
            originalDockAreas = new System.Runtime.CompilerServices.ConditionalWeakTable<DockContent, DockingDefaults>();

        private sealed class DockingDefaults
        {
            internal readonly DockAreas Areas;
            internal DockingDefaults(DockAreas areas) { Areas = areas; }
        }

        public static void OpenSession(SessionDataStartInfo ssi)
        {
            if (MainForm.InvokeRequired)
            {
                MainForm.BeginInvoke(new Action<SessionDataStartInfo>(OpenSession), ssi);
                return;
            }

            if (ssi != null)
            {
                if (ssi.UseScp || IsScpSession(ssi.Session))
                {
                    OpenScpSession(ssi.Session);
                }
                else
                {
                    OpenProtoSession(ssi.Session);
                }
            }
        }

        /// <summary>Import sessions from the specified file into the in-application database</summary>
        /// <param name="fileName">A string containing the path of the filename that holds session configuration</param>
        public static void ImportSessionsFromFile(string fileName)
        {
            if (fileName == null) { return; }
            if (File.Exists(fileName))
            {
                Log.InfoFormat($"Importing sessions from file, path={fileName}");
                List<SessionData> sessions = SessionData.LoadSessionsFromFile(fileName);
                ImportSessions(sessions, "Imported");
            }
        }

        internal static async Task ImportSessionsFromFileAsync(string fileName, CancellationToken cancellation)
        {
            // An import is a separate read, not acceptance of a new save baseline.
            var catalogStorage = sessionStorage;
            var imported = await new SessionStorage().LoadAsync(fileName, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (!ReferenceEquals(sessionStorage, catalogStorage))
                throw new InvalidOperationException("The session catalog changed during the import. Start the import again.");
            await ImportSessionsAsync(imported, "Imported");
        }

        /// <summary>Import sessions from the specified folder into the in-application database</summary>
        /// <param name="folderName">A string containing the path of the folder that holds session configuration files</param>
        public static void ImportSessionsFromFolder(string folderName)
        {
            if (folderName == null) { return; }
            if (Directory.Exists(folderName))
            {
                Log.InfoFormat($"Importing sessions from folder, path={folderName}");
                List<SessionData> sessions = SessionData.LoadSessionsFromFolder(folderName);
                ImportSessions(sessions, "ImportedFromPortablePuTTY");
            }
        }

        /// <summary>Import sessions from Windows Registry which were set by PuTTY or KiTTY and load them into the in-application sessions database</summary>
        public static void ImportSessionsFromPuTTY()
        {
            Log.InfoFormat("Importing sessions from PuTTY/KiTTY");
            List<SessionData> sessions = PuttyDataHelper.GetAllSessionsFromPuTTY();
            ImportSessions(sessions, "ImportedFromPuTTY");
        }

        /// <summary>Import sessions from Windows Registry which were set by PuttYCM and load them into the in-application sessions database</summary>
        public static void ImportSessionsFromPuttyCM(string fileExport)
        {
            Log.InfoFormat("Importing sessions from PuttyCM");
            List<SessionData> sessions = PuttyDataHelper.GetAllSessionsFromPuTTYCM(fileExport);
            ImportSessions(sessions, "ImportedFromPuTTYCM");
        }

        /// <summary>Import sessions from Windows Registry which were set by PuTTY or KiTTY and load them into the in-application sessions database</summary>
        public static void ImportRDPSessionsFromWinReg()
        {
            Log.InfoFormat("Importing RDP sessions from Windows registry");
            List<SessionData> sessions = RDPDataHelper.GetAllSessionsFromRegistry();
            ImportSessions(sessions, "ImportRDPSessionsFromWinReg");
        }

        /// <summary>Import sessions from a from a List object into the specified folder</summary>
        /// <param name="sessions">A List of <seealso cref="SessionData"/> objects</param>
        /// <param name="folder">The destination folder name</param>
        public static void ImportSessions(List<SessionData> sessions, string folder)
        {
            ImportSessions(sessions, folder, SaveSessions);
        }

        internal static void ImportSessions(List<SessionData> sessions, string folder, Action persistSessions)
        {
            ImportSessionsCoreAsync(sessions, folder, AsCompletedTask(persistSessions)).GetAwaiter().GetResult();
        }

        internal static Task ImportSessionsAsync(List<SessionData> sessions, string folder)
        {
            return ImportSessionsCoreAsync(sessions, folder, SaveSessionsAsync);
        }

        private static async Task ImportSessionsCoreAsync(List<SessionData> sessions, string folder, Func<Task> persistSessions)
        {
            EnsureSessionCatalogReady();
            if (sessions == null)
            {
                throw new ArgumentNullException("sessions");
            }
            if (persistSessions == null)
            {
                throw new ArgumentNullException("persistSessions");
            }

            List<SessionData> addedSessions = new List<SessionData>();
            Dictionary<SessionData, string[]> originalNames = sessions.ToDictionary(
                session => session,
                session => new[] { session.SessionId, session.SessionName });

            try
            {
                foreach (SessionData session in sessions)
                {
                    // Prepend the destination folder and avoid collisions with existing sessions
                    // and sessions added earlier in this import.
                    session.SessionId = MakeUniqueSessionId(SessionData.CombineSessionIds(folder, session.SessionId));
                    session.SessionName = SessionData.GetSessionNameFromId(session.SessionId);
                    if (!AddSession(session))
                    {
                        throw new InvalidOperationException("Could not add imported session '" + session.SessionId + "'.");
                    }
                    addedSessions.Add(session);
                }

                await persistSessions();
                Log.InfoFormat($"Imported {sessions.Count} sessions into {folder}");
            }
            catch
            {
                foreach (SessionData session in addedSessions)
                {
                    RemoveSession(session.SessionId);
                }
                foreach (KeyValuePair<SessionData, string[]> original in originalNames)
                {
                    original.Key.SessionId = original.Value[0];
                    original.Key.SessionName = original.Value[1];
                }
                throw;
            }
        }

        internal static bool IsScpSession(SessionData session)
        {
            return session != null && session.Proto == ConnectionProtocol.SCP;
        }

        /// <summary>Import sessions from older version of SuperPuTTY from the Windows Registry</summary>
        public static void ImportSessionsFromSuperPutty1030()
        {
            try
            {
                List<SessionData> sessions = SessionData.LoadSessionsFromRegistry();
                if (sessions != null && sessions.Count > 0)
                {
                    foreach (SessionData session in sessions)
                    {
                        AddSession(session);
                    }
                    SaveSessions();

                    Log.InfoFormat($"Imported {sessions.Count} old sessions from registry.");
                }
            }
            catch (Exception ex)
            {
                Log.WarnFormat($"Could not import old sessions, msg={ex.Message}");
            }
        }

        /// <summary>Generate a unique session ID to prevent collisions in the in-application data store, used when importing and merging sessions from
        /// another application or an older versin of SuperPuTTY</summary>
        /// <param name="sessionId">A string containing the sessionID of the session being imported</param>
        /// <returns>A string containing a unique sessionID</returns>
        public static string MakeUniqueSessionId(string sessionId)
        {
            string newSessionId = sessionId;

            for (int i = 1; i < 1000; i++)
            {
                if (!SessionPathExists(newSessionId))
                {
                    break;
                }                
                newSessionId = string.Format("{0}-{1}", sessionId, i);
            }

            return newSessionId;
        }

        #endregion

        #region Icons

        /// <summary>Load Images from themes folder</summary>
        /// <param name="theme">the name of the theme folder</param>
        /// <param name="isEnableStopImage">enable special stop image</param>
        public static ImageList LoadImageList(string theme, bool isEnableStopImage)
        {
            // 32-bit keeps the alpha channel of the png icons so they blend with any background
            ImageList imgIcons = new ImageList { ColorDepth = ColorDepth.Depth32Bit };

            // icons without alpha have a white box background that looks wrong on the dark theme
            Func<Image, Image> prepare = img => SuperPutty.Gui.ThemeManager.IsDark
                ? SuperPutty.Gui.ThemeManager.WithTransparentBackground(img)
                : img;

            if (isEnableStopImage)
                imgIcons.Images.Add("stop", prepare(Resources.stop));
            // Load the 2 standard icons in case no icons exist in icons directory, these will be used.
            imgIcons.Images.Add(SessionTreeview.ImageKeyFolder, prepare(Resources.folder));
            imgIcons.Images.Add(SessionTreeview.ImageKeySession, prepare(Resources.computer));

            try
            {
                string themeFolder = Directory.GetCurrentDirectory();
                themeFolder = Path.Combine(themeFolder, "themes");
                themeFolder = Path.Combine(themeFolder, theme);
                themeFolder = Path.Combine(themeFolder, "icons");

                if (Directory.Exists(themeFolder))
                {
                    foreach (FileInfo fi in new DirectoryInfo(themeFolder).GetFiles())
                    {
                        if (Regex.IsMatch(fi.Extension, @"\.(bmp|jpg|jpeg|png)", RegexOptions.IgnoreCase))
                        {
                            Image img = Image.FromFile(fi.FullName);
                            imgIcons.Images.Add(Path.GetFileNameWithoutExtension(fi.Name), prepare(img));
                        }
                    }
                    Log.InfoFormat($"Loaded {imgIcons.Images.Count} icons from theme directory.  dir={themeFolder}");
                }
                else
                {
                    Log.WarnFormat($"theme directory not found, no images loaded. dir={theme}");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error while loading icons.", ex);
            }


            return imgIcons;
        }

        /// <summary>Get the Icon defined for the specified session</summary>
        /// <param name="session">The session configuration data</param>
        /// <returns>The Icon configured for the session</returns>
        public static Icon GetIconForSession(SessionData session)
        {
            Icon icon = null;
            if (session != null)
            {
                string imageKey = session.ImageKey == null || !Images.Images.ContainsKey(session.ImageKey)
                    ? SessionTreeview.ImageKeySession : session.ImageKey;
                try
                {
                    Image img = Images.Images[imageKey];
                    Bitmap bmp = img as Bitmap;
                    if (bmp != null)
                    {
                        icon = Icon.FromHandle(bmp.GetHicon());
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Error getting icon for image", ex);
                }
            }
            return icon;
        }

        private static void ApplyIconForWindow(ToolWindow win, SessionData session)
        {
            win.Icon = GetIconForSession(session);
        }

        #endregion

        #region Properties
        /// <summary>true if the application has not defined where the required putty program is located</summary>
        public static bool IsFirstRun {
            get
            {
                if (isFirstRun == null)
                {
                    isFirstRun = string.IsNullOrEmpty(Settings.PuttyExe);
                }
                return isFirstRun.Value;
            }
        }

        /// <summary>true if the application has not defined where the putty scp program is located</summary>
        public static bool IsScpEnabled { get { return File.Exists(SuperPuTTY.Settings.PscpExe); } }

        /// <summary>true if the application has defined where the filezilla program is located</summary>
        public static bool IsFilezillaEnabled { get { return File.Exists(SuperPuTTY.Settings.FileZillaExe); } }

        /// <summary>true if the application has defined where the winSCP program is located</summary>
        public static bool IsWinSCPEnabled { get { return File.Exists(SuperPuTTY.Settings.WinSCPExe); } }

        /// <summary>Returns a string containing the current version of SuperPuTTY</summary>
        public static string Version { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); } }
        public static string DisplayVersion
        {
            get
            {
                var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                return info != null ? info.InformationalVersion : Version;
            }
        }

        internal static Settings Settings { get { return Settings.Default; } }
        public static frmSuperPutty MainForm { get; set; }        
        public static string LayoutsDir { get { return Path.Combine(Settings.SettingsFolder, "layouts"); } }
        public static LayoutData CurrentLayout { get; private set; }
        public static LayoutData StartingLayout { get; private set; }
        public static SessionDataStartInfo StartingSession { get; private set; }
        public static BindingList<LayoutData> Layouts { get { return layouts; } }
        public static BindingList<SessionData> Sessions { get { return sessionsList; } }
        public static CommandLineOptions CommandLine { get; private set; }
        public static ImageList Images { get; private set; }
        public static ImageList ImagesWithStop { get; private set; }
        public static GlobalWindowEvents WindowEvents { get; private set; }

        /// <summary>true of KiTTY is being used instead of putty</summary>
        public static bool IsKiTTY
        {
            get
            {
                bool isKitty = false;
                if (File.Exists(Settings.PuttyExe))
                {
                    string exe = Path.GetFileName(Settings.PuttyExe);
                    isKitty = exe != null && exe.ToLower().StartsWith("kitty");
                }
                return isKitty;
            }
        }

        public static string PuTTYAppName
        {
            get
            {
                if (IsKiTTY)
                    return "KiTTY";

                return "PuTTY";
            }
        }

        /// <summary>The path to the automatic window-layout configuration.</summary>
        public static string AutoRestoreLayoutPath { get { return Path.Combine(Settings.SettingsFolder, LayoutData.AutoRestoreLayoutFileName); } }

        #endregion
    }

    #region SuperPuttyAction
    public enum SuperPuttyAction
    {
        CloseTab,
        NextTab,
        PrevTab,
        Options,
        FullScreen,
        OpenSession,
        SwitchSession,
        DuplicateSession,
        GotoCommandBar,
        GotoConnectionBar,
        FocusActiveSession,
        /// <summary>Open Script Editor Window</summary>
        OpenScriptEditor,
        /// <summary>Rename active tab</summary>
        RenameTab,
        RestartSession
    } 
    #endregion

}
