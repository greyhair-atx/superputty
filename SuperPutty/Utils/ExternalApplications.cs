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
            if (!String.IsNullOrEmpty(session.Password) && !SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg)
                Log.Warn("SuperPuTTY is set to NOT allow the use of the -pw <password> argument, this can be overriden in Tools -> Options -> GUI");

            string args = BuildArguments(session, false, true);
            Log.Debug("Send to FileZilla: " + BuildArguments(session, false, false));
            Process.Start(SuperPuTTY.Settings.FileZillaExe, args);
        }

        /// <summary>
        /// Open the WinSCP program with the sesion data, for sftp connection. 
        /// </summary>
        /// <param name="session"></param>
        public static void openWinSCP(SessionData session)
        {
            if (!String.IsNullOrEmpty(session.Password) && !SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg)
                Log.Warn("SuperPuTTY is set to NOT allow the use of the -pw <password> argument, this can be overriden in Tools -> Options -> GUI");

            string args = BuildArguments(session, true, true);
            Log.Debug("Send to WinSCP: " + BuildArguments(session, true, false));
            Process.Start(SuperPuTTY.Settings.WinSCPExe, args);
        }

    }
}
