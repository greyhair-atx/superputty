using System;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Web;
using log4net;
using System.Xml;
using System.IO;
using System.Linq;

namespace SuperPutty.Data
{
    /// <summary>Helper methods used mostly for importing settings and session data from other applications</summary>
    public class RDPDataHelper
    {
        public static readonly ILog Log = LogManager.GetLogger(typeof(RDPDataHelper));

        public static RegistryKey RootAppKey
        {
            get
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Terminal Server Client\Default");
                return key;
            }
        }

        public static List<SessionData> GetAllSessionsFromRegistry()
        {
            List<SessionData> sessions = new List<SessionData>();

            using (RegistryKey key = RootAppKey)
            {
                if (key == null) return sessions;
                foreach (string keyName in key.GetValueNames())
                {
                    if (!keyName.StartsWith("MRU", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        string endpoint = key.GetValue(keyName, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                        sessions.Add(CreateSession(endpoint));
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is IOException ||
                        ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                    {
                        Log.Warn("Skipping invalid or inaccessible RDP history entry: " + keyName, ex);
                    }
                }
            }

            return sessions;
        }

        internal static SessionData CreateSession(string endpoint)
        {
            if (String.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("An RDP server address is required.");
            string host = endpoint.Trim();
            string portText = null;
            int port = 3389;
            if (host.StartsWith("[", StringComparison.Ordinal))
            {
                int close = host.IndexOf(']');
                if (close < 0) throw new ArgumentException("Invalid IPv6 server address.");
                string suffix = host.Substring(close + 1);
                if (suffix.Length > 0)
                {
                    if (!suffix.StartsWith(":", StringComparison.Ordinal)) throw new ArgumentException("Invalid RDP port.");
                    portText = suffix.Substring(1);
                }
                host = host.Substring(1, close - 1);
            }
            else if (host.IndexOf(':') >= 0 && host.IndexOf(':') == host.LastIndexOf(':'))
            {
                int separator = host.IndexOf(':');
                portText = host.Substring(separator + 1);
                host = host.Substring(0, separator);
            }
            if (portText != null && (!int.TryParse(portText, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535))
                throw new ArgumentException("Invalid RDP port.");
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
                throw new ArgumentException("Invalid RDP server address.");
            return new SessionData
            {
                Host = host,
                Port = port,
                Proto = ConnectionProtocol.RDP,
                PuttySession = endpoint,
                SessionName = endpoint,
                Username = ""
            };
        }

    }
}
