/*
 * Copyright (c) 2017 Anish Sane https://stackoverflow.com/users/793796/anishsane
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
using SuperPutty.Data;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SuperPutty.Utils
{

    /// <summary>
    /// Helper class for RDP support
    /// </summary>
    public class RDPStartInfo
    {
        private const string SmartSizingFileName = "smart-sizing.rdp";
        private const string SmartSizingFileContents =
            "screen mode id:i:1\r\n" +
            "smart sizing:i:1\r\n" +
            "use multimon:i:0\r\n";
        private static readonly object SmartSizingFileLock = new object();

        public RDPStartInfo(SessionData session, String binName)
        {
            if (IsFreeRdpExecutable(binName))
            {
                this.Args = BuildFreeRdpArgs(session);
            }
            else
            {
                this.Args = BuildMstscArgs(session);
            }

            this.StartingDir = "%userprofile%\\Desktop";
        }

        public string Args { get; set; }
        public string StartingDir { get; set; }

        public static bool IsFreeRdpExecutable(string binName)
        {
            return String.Equals(Path.GetFileName(binName), "wfreerdp.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildFreeRdpArgs(SessionData session)
        {
            StringBuilder args = new StringBuilder(
                "/size:1920x1080 /smart-sizing +window-drag /disp /workarea " +
                "/network:wan +auto-reconnect /auto-reconnect-max-retries:0 +bitmap-cache");

            if (session.IgnoreRdpCertificateErrors)
                args.Append(" /cert-ignore");

            string endpoint = session.Host ?? String.Empty;
            if (session.Port != 0)
                endpoint += ":" + session.Port;
            args.Append(" ").Append(CommandLineOptions.QuoteArgument("/v:" + endpoint));

            if (!String.IsNullOrEmpty(session.Username))
                args.Append(" ").Append(CommandLineOptions.QuoteArgument("/u:" + session.Username));

            string extraArgs = RemoveCertificateBypassArguments(session.ExtraArgs);
            if (!String.IsNullOrWhiteSpace(extraArgs))
                args.Append(" ").Append(extraArgs);

            return args.ToString();
        }

        private static string RemoveCertificateBypassArguments(string extraArgs)
        {
            if (String.IsNullOrWhiteSpace(extraArgs))
                return String.Empty;

            return Regex.Replace(
                extraArgs,
                @"(?i)(?:^|\s)/(?:cert-ignore|cert:ignore)(?=\s|$)",
                " ").Trim();
        }

        private static string BuildMstscArgs(SessionData session)
        {
            StringBuilder args = new StringBuilder();
            args.Append('"').Append(GetSmartSizingConnectionFile()).Append('"');
            string endpoint = session.Host ?? String.Empty;
            if (session.Port != 0)
                endpoint += ":" + session.Port;
            args.Append(" ").Append(CommandLineOptions.QuoteArgument("/v:" + endpoint));

            if (!String.IsNullOrWhiteSpace(session.ExtraArgs))
                args.Append(" ").Append(session.ExtraArgs.Trim());

            return args.ToString();
        }

        /// <summary>
        /// Creates the reusable MSTSC connection profile that enables scaling the
        /// remote desktop to the size of its embedded client window.
        /// </summary>
        private static string GetSmartSizingConnectionFile()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPuTTY");
            string fileName = Path.Combine(directory, SmartSizingFileName);

            lock (SmartSizingFileLock)
            {
                Directory.CreateDirectory(directory);
                if (!File.Exists(fileName) || File.ReadAllText(fileName) != SmartSizingFileContents)
                {
                    File.WriteAllText(fileName, SmartSizingFileContents, Encoding.Unicode);
                }
            }

            return fileName;
        }

    }
}
