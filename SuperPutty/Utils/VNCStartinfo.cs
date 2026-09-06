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
using log4net;
using SuperPutty.Data;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;

namespace SuperPutty.Utils
{

    /// <summary>
    /// Helper class for VNC support
    /// </summary>
    public class VNCStartInfo
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(VNCStartInfo));

        public VNCStartInfo(SessionData session)
            : this(session, PuttyStartInfo.GetExecutable(session))
        {
        }

        public VNCStartInfo(SessionData session, string executable)
        {
            bool tigerVnc = IsTigerVncExecutable(executable);
            this.Args = tigerVnc ? "" : "-scale=auto ";
            this.ArgsForLog = this.Args;

            if (!tigerVnc && session.Port != 0)
            {
                this.Args += "-port=" + session.Port.ToString() + " ";
                this.ArgsForLog += "-port=" + session.Port.ToString() + " ";
            }

            if (!String.IsNullOrEmpty(session.Password))
            {
                if (tigerVnc)
                {
                    Log.Info("TigerVNC will prompt for authentication; saved plaintext passwords are not passed to this viewer");
                }
                else if (SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg)
                {
                    this.Args += "-password=" + CommandLineOptions.QuoteArgument(session.Password) + " ";
                    this.ArgsForLog += "-password=XXXXX ";
                }
                else
                {
                    Log.Warn("VNC password was not placed on the command line because plaintext password arguments are disabled");
                }
            }

            if (!String.IsNullOrEmpty(session.ExtraArgs))
            {
                string safeExtraArgs = CommandLineOptions.RemoveSensitiveArguments(session.ExtraArgs);
                if (!String.IsNullOrEmpty(safeExtraArgs))
                {
                    this.Args += safeExtraArgs + " ";
                    this.ArgsForLog += safeExtraArgs + " ";
                }
            }

            string endpoint = session.Host ?? String.Empty;
            if (tigerVnc && session.Port != 0)
            {
                if (endpoint.Contains(":") && !endpoint.StartsWith("["))
                    endpoint = "[" + endpoint + "]";
                endpoint += "::" + session.Port;
            }
            this.Args += CommandLineOptions.QuoteArgument(endpoint);
            this.ArgsForLog += CommandLineOptions.QuoteArgument(endpoint);

            this.StartingDir = "%userprofile%\\Desktop";
        }

        internal static bool IsTigerVncExecutable(string executable)
        {
            if (String.IsNullOrEmpty(executable))
                return false;

            if (executable.IndexOf("tigervnc", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // The Windows viewer is also distributed as vncviewer.exe.
            if (File.Exists(executable))
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(executable);
                return ((version.ProductName ?? "") + " " + (version.FileDescription ?? ""))
                    .IndexOf("tigervnc", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }

        public string Args { get; set; }
        public string ArgsForLog { get; private set; }
        public string StartingDir { get; set; }

    }
}
