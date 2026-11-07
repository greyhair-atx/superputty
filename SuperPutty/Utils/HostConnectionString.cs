using System;
using SuperPutty.Data;
using log4net;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Helper class to parse host connection strings (e.g. ssh://localhost:2222)
    /// </summary>
    public class HostConnectionString
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(HostConnectionString));

        public HostConnectionString(string hostString, bool ignorePort = false)
        {

            int idx = hostString.IndexOf("://");
            string hostPort = hostString;
            if (idx != -1)
            {
                // ssh://localhost:2020, or ssh2://localhost:2020
                this.Protocol = (ConnectionProtocol)Enum.Parse(typeof(ConnectionProtocol), hostString.Substring(0, idx), true);
                if (this.Protocol == ConnectionProtocol.SSH2)
                {
                    // ssh2 is accepted only as a compatibility feature
                    this.Protocol = ConnectionProtocol.SSH;
                }
                hostPort = hostString.Substring(idx + 3);
            }

            // Firefox addes a '/'...
            hostPort = hostPort.TrimEnd('/');
            this.Host = hostPort;
            if (!ignorePort)
            {
                int separator = hostPort.LastIndexOf(':');
                bool bracketed = hostPort.StartsWith("[", StringComparison.Ordinal);
                int closeBracket = bracketed ? hostPort.IndexOf(']') : -1;
                bool hasPort = bracketed ? closeBracket > 0 && separator == closeBracket + 1
                    : separator >= 0 && hostPort.IndexOf(':') == separator;
                int port;
                if (hasPort && int.TryParse(hostPort.Substring(separator + 1), out port) && port > 0 && port <= 65535)
                {
                    this.Host = hostPort.Substring(0, separator).Trim('[', ']');
                    this.Port = port;
                }
                else if (bracketed && closeBracket == hostPort.Length - 1)
                    this.Host = hostPort.Substring(1, closeBracket - 1);
            }


            log.InfoFormat(
                "Parsed[{0}]: proto={1}, host={2}, port={3}", 
                hostString, 
                this.Protocol.HasValue ? this.Protocol.ToString() : "", 
                this.Host, 
                this.Port.HasValue ? this.Port.ToString() : "");
        }

        public ConnectionProtocol? Protocol { get; set; }
        public string Host { get; set; }
        public int? Port { get; set; }
    }
}
