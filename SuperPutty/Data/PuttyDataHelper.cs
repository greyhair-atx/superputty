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
    public class PuttyDataHelper
    {
        public static readonly ILog Log = LogManager.GetLogger(typeof(PuttyDataHelper));

        public const string SessionDefaultSettings = "Default Settings";
        public const string SessionEmptySettings = "";

        public static RegistryKey RootAppKey
        {
            get
            {
                RegistryKey key = SuperPuTTY.IsKiTTY
                    ? Registry.CurrentUser.OpenSubKey(@"Software\9bis.com\KiTTY\Sessions")
                    : Registry.CurrentUser.OpenSubKey(@"Software\SimonTatham\PuTTY\Sessions");
                return key;
            }
        }
        public static List<string> GetSessionNames()
        {
            List<string> names = new List<string> {SessionEmptySettings};
            RegistryKey key = RootAppKey;
            if (key != null)
            {
                string[] savedSessionNames = key.GetSubKeyNames();
                names.AddRange(savedSessionNames.Select(HttpUtility.UrlDecode));
            }

            if (!names.Contains(SessionDefaultSettings))
            {
                names.Insert(1, SessionDefaultSettings);
            }

            return names;
        }

        public static List<SessionData> GetAllSessionsFromPuTTY()
        {
            using (RegistryKey key = RootAppKey)
                return ReadRegistrySessions(key);
        }

        internal static List<SessionData> ReadRegistrySessions(RegistryKey key)
        {
            var sessions = new List<SessionData>();
            if (key == null) return sessions;
            foreach (string keyName in key.GetSubKeyNames())
            {
                try
                {
                    using (RegistryKey sessionKey = key.OpenSubKey(keyName))
                    {
                        if (sessionKey == null) continue;
                        object port = sessionKey.GetValue("PortNumber", 22);
                        ConnectionProtocol protocol;
                        if (!(port is int) || (int)port < 1 || (int)port > 65535 ||
                            !TryParseProtocol(sessionKey.GetValue("Protocol", "SSH") as string, out protocol))
                            throw new InvalidDataException("Invalid port or unsupported protocol.");
                        sessions.Add(new SessionData
                        {
                            Host = ReadRegistryString(sessionKey, "HostName", ""),
                            Port = (int)port,
                            Proto = protocol,
                            PuttySession = ReadRegistryString(sessionKey, "PuttySession", HttpUtility.UrlDecode(keyName)),
                            SessionName = HttpUtility.UrlDecode(keyName),
                            Username = ReadRegistryString(sessionKey, "UserName", "")
                        });
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                {
                    Log.Warn("Skipping invalid or inaccessible PuTTY session: " + keyName, ex);
                }
            }
            return sessions;
        }

        private static string ReadRegistryString(RegistryKey key, string name, string fallback)
        {
            string value = key.GetValue(name, fallback) as string;
            if (value == null) throw new InvalidDataException("Invalid string value: " + name);
            return value;
        }

        private static bool TryParseProtocol(string value, out ConnectionProtocol protocol)
        {
            return Enum.TryParse(value, true, out protocol) && Enum.IsDefined(typeof(ConnectionProtocol), protocol);
        }

        private static string RequiredValue(XmlNode parent, string path)
        {
            XmlNode node = parent.SelectSingleNode(path);
            if (node == null) throw new InvalidDataException("PuTTYCM export is missing " + path + ".");
            return node.InnerText;
        }

        public static List<SessionData> GetAllSessionsFromPuTTYCM(string fileExport)
        {
            List<SessionData> sessions = new List<SessionData>();

            if (fileExport == null || !File.Exists(fileExport))
            {
                return sessions;
            }

            XmlDocument doc = new XmlDocument { XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(fileExport, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                doc.Load(reader);


            XmlNodeList connections = doc.DocumentElement.SelectNodes("//connection[@type='PuTTY']");
            foreach (XmlElement connection in connections)
            {
                List<string> folders = new List<string>();
                XmlElement node = connection.ParentNode as XmlElement;
                while (node != null && node.Name != "root")
                {
                    if (node.Name == "container" && node.GetAttribute("type") == "folder")
                    {
                        folders.Add(node.GetAttribute("name"));
                    }
                    node = node.ParentNode as XmlElement;
                }
                folders.Reverse();
                string parentPath = string.Join("/", folders.ToArray());

                int port;
                ConnectionProtocol protocol;
                if (!int.TryParse(RequiredValue(connection, "connection_info/port"), out port) || port < 1 || port > 65535)
                    throw new InvalidDataException("PuTTYCM export contains an invalid port.");
                if (!TryParseProtocol(RequiredValue(connection, "connection_info/protocol"), out protocol))
                    throw new InvalidDataException("PuTTYCM export contains an unsupported protocol.");
                string name = RequiredValue(connection, "connection_info/name");
                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException("PuTTYCM export contains an empty session name.");
                SessionData session = new SessionData
                {
                    SessionName = name,
                    Host = RequiredValue(connection, "connection_info/host"),
                    Port = port,
                    Proto = protocol,
                    PuttySession = RequiredValue(connection, "connection_info/session"),
                    Username = RequiredValue(connection, "login/login")
                };
                session.SessionId = string.IsNullOrEmpty(parentPath)
                    ? session.SessionName
                    : SessionData.CombineSessionIds(parentPath, session.SessionName);

                sessions.Add(session);
            }

            return sessions;
        }
    }
}
