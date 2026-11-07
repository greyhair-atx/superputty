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
        static BindingList<SessionData> sessionsList = new BindingList<SessionData>();
        static bool? isFirstRun;

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
                LoadSessions();                

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

            // Register IpcChanncel for single instance support
            SingleInstanceHelper.RegisterRemotingService();
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
                try
                {
                    SettingsFolderResolver.CopyExistingSettings(configuredFolder, writableFolder);
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not copy existing session and layout files to the Local AppData fallback", ex);
                }
            }

            if (!String.Equals(Settings.SettingsFolder, writableFolder, StringComparison.OrdinalIgnoreCase))
            {
                Settings.SettingsFolder = writableFolder;
                Settings.Save();
            }
        }

        /// <summary>Called when application is shutting down, sends message to log.</summary>
        public static void Shutdown()
        {
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
                    File.Delete(layout.FilePath);
                }
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
            File.Move(fileOld, fileNew);
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
                    File.Move(fileNew, fileOld);
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
                    List<LayoutData> newLayouts = Directory.GetFiles(LayoutsDir).Select(file => new LayoutData(file)).ToList();

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

        public static void LoadLayout(LayoutData layout)
        {
            LoadLayout(layout, false);
        }

        public static void LoadLayout(LayoutData layout, bool isNewLayoutAlreadyActive)
        {
            Log.InfoFormat("LoadLayout: layout={0}, isNewLayoutAlreadyActive={1}", layout == null ? "NULL" : layout.Name, isNewLayoutAlreadyActive);
            LayoutChangedEventArgs args = new LayoutChangedEventArgs
            {
                New = layout,
                Old = CurrentLayout,
                IsNewLayoutAlreadyActive = isNewLayoutAlreadyActive
            };

            try
            {
                IsLayoutChanging = true;

                if (LayoutChanging != null)
                {
                    LayoutChanging(typeof(SuperPuTTY), args);
                }

            }
            finally
            {
                IsLayoutChanging = false;
            }


            if (LayoutChanged != null)
            {
                CurrentLayout = layout;
                LayoutChanged(typeof(SuperPuTTY), args);
            }
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

        /// <summary>Load sessions database from file into the application</summary>
        public static void LoadSessions()
        {
            string fileName = SessionsFileName;
            Log.InfoFormat($"Loading all sessions.  file={fileName}");

            try
            {
                if (File.Exists(fileName))
                {
                    ReplaceSessionCatalog(SessionData.LoadSessionsFromFile(fileName));
                }
                else
                {
                    Log.WarnFormat($"Sessions file does not exist, nothing loaded.  file={fileName}");
                }

            }
            catch (Exception ex)
            {
                Log.Error($"Error while loading sessions from {fileName}", ex);
            }
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
                persistSessions();
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
                if (notify) sessionsList.ResetBindings();
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

        /// <summary>Save in-application Session Database to XML File</summary>
        public static void SaveSessions()
        {
            Log.InfoFormat("Saving all sessions");
            SessionData.SaveSessionsToFile(GetAllSessions(), SessionsFileName);
        }

        /// <summary>
        /// Remove a session from the in-application sessions database. 
        /// </summary>
        /// <param name="sessionId">The <seealso cref="SessionData.SessionId"/> of the session to remove</param>
        /// <returns>true on success, or false on failure or if session did not exist</returns>
        public static bool RemoveSession(string sessionId)
        {
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

        /// <summary>Open a new putty window with its settings being passed in a <seealso cref="SessionData"/> object</summary>
        /// <param name="session">The <seealso cref="SessionData"/> object containing the settings</param>
        public static ctlPuttyPanel OpenProtoSession(SessionData session)
        {
            if (IsScpSession(session))
            {
                OpenScpSession(session);
                return null;
            }

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
                    if (panel != null)
                    {
                        // save the last dockstate (if it has been changed)
                        if (session.LastDockstate != panel.DockState
                            && panel.DockState != DockState.Unknown
                            && panel.DockState != DockState.Hidden)
                        {
                            session.LastDockstate = panel.DockState;
                            SaveSessions();
                        }

                        if (panel.InvokeRequired)
                        {
                            panel.BeginInvoke((MethodInvoker)delegate {
                                panel.CloseWithoutConfirmation();
                            });
                        }
                        else
                        {
                            panel.CloseWithoutConfirmation();
                        }
                    }
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

                persistSessions();
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
