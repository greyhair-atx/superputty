using System;
using log4net;
using SuperPutty.Data;
using System.Diagnostics;
using System.Linq;

namespace SuperPutty.Utils
{
    public static class ExternalApplications
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ExternalApplications));

        internal static string BuildArguments(SessionData session, bool winScp, bool includePassword)
        {
            if (session == null) throw new ArgumentNullException("session");
            string host = session.Host ?? String.Empty;
            if (Uri.CheckHostName(host.Trim('[', ']')) == UriHostNameType.Unknown)
                throw new ArgumentException("A valid server hostname or IP address is required.");
            if (host.Contains(":") && !host.StartsWith("[", StringComparison.Ordinal))
                host = "[" + host + "]";
            string user = Uri.EscapeDataString(session.Username ?? String.Empty);
            string credentials = user;
            if (user.Length > 0 && includePassword && SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg &&
                !String.IsNullOrEmpty(session.Password))
                credentials += ":" + Uri.EscapeDataString(session.Password);
            if (credentials.Length > 0)
                credentials += "@";
            string path = session.RemotePath ?? String.Empty;
            if (path.Length > 0 && !path.StartsWith("/", StringComparison.Ordinal))
                path = "/" + path;
            if (winScp && path.Length > 0 && !path.EndsWith("/", StringComparison.Ordinal))
                path += "/";
            path = String.Join("/", path.Split('/').Select(Uri.EscapeDataString));
            string args = CommandLineOptions.QuoteArgument("sftp://" + credentials + host + ":" + session.Port + path);
            if (!String.IsNullOrEmpty(session.LocalPath))
                args += winScp
                    ? " -rawsettings " + CommandLineOptions.QuoteArgument("LocalDirectory=" + session.LocalPath)
                    : " " + CommandLineOptions.QuoteArgument("--local=" + session.LocalPath);
            return args;
        }

        /// <summary>
        /// Open the filezilla program with the sesion data, for sftp connection. 
        /// </summary>
        /// <param name="session"></param>
        public static void openFileZilla(SessionData session)
        {
            OpenClient(session, false, SuperPuTTY.Settings.FileZillaExe);
        }

        /// <summary>
        /// Open the WinSCP program with the sesion data, for sftp connection. 
        /// </summary>
        /// <param name="session"></param>
        public static void openWinSCP(SessionData session)
        {
            OpenClient(session, true, SuperPuTTY.Settings.WinSCPExe);
        }


        private static void OpenClient(SessionData session, bool winScp, string executable)
        {
            string error;
            if (!TryOpenClient(session, winScp, executable, out error))
                System.Windows.Forms.MessageBox.Show(error, "Open " + (winScp ? "WinSCP" : "FileZilla"),
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
        }

        internal static bool TryOpenClient(SessionData session, bool winScp, string executable,
            out string error, Action<string, string> start = null)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(executable))
                    throw new ArgumentException("Configure the external client executable in Tools > Options.");
                string args = BuildArguments(session, winScp, true);
                Log.Debug("Send to " + (winScp ? "WinSCP: " : "FileZilla: ") + BuildArguments(session, winScp, false));
                if (start != null) start(executable, args);
                else using (Process process = Process.Start(executable, args)) { }
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.ComponentModel.Win32Exception ||
                ex is InvalidOperationException || ex is System.IO.IOException ||
                ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                Log.Warn("Unable to open external file-transfer client (" + ex.GetType().Name + ").");
                error = "Unable to open the file-transfer client. Check the session hostname and the executable configured in Tools > Options.";
                return false;
            }
        }

    }
}
