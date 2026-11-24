/*
 * Copyright (c) 2009 Jim Radford http://www.jimradford.com
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
using System.Windows.Forms;
using System.Threading;
using log4net;
using System.Configuration;
using SuperPutty.Utils;
using System.Reflection;
using System.IO;
using System.Drawing;
using System.Text;

namespace SuperPutty
{
    static class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Program));

        private static bool EnforceSingleInstance = Convert.ToBoolean(
            ConfigurationManager.AppSettings["SuperPuTTY.SingleInstance"] ?? "False");
        
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // The isolated PSCP console host must not initialize settings, logging or single-instance routing.
            if (Scp.InteractivePscpDialog.TryRunHost(args)) return;
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            // send log to console
            log4net.Config.BasicConfigurator.Configure();

            SingleInstanceMutex instance = null;
            IDisposable listener = null;
            try
            {
                bool firstRun = SuperPuTTY.IsFirstRun;
                bool singleInstance = (EnforceSingleInstance || SuperPuTTY.Settings.SingleInstanceMode) && !firstRun;
                SingleInstanceEndpoint endpoint = null;
                instance = StartSingleInstanceComponent(singleInstance, () =>
                {
                    endpoint = SingleInstanceEndpoint.Current;
                    return new SingleInstanceMutex(endpoint);
                });

                Log.InfoFormat(
                    "IsFirstRun={0}, SingleInstanceMode={1}, onlyInstance={2}",
                    firstRun, SuperPuTTY.Settings.SingleInstanceMode, instance?.IsPrimary == true);
                bool legacyInstance = singleInstance && SingleInstanceMutex.LegacyInstanceExists();
                if (singleInstance && (!instance.IsPrimary || legacyInstance))
                {
                    // Never send credentials through the old unauthenticated
                    // remoting channel when an older executable is still running.
                    if (legacyInstance || !SingleInstanceHelper.TryLaunchInExistingInstance(args))
                    {
                        MessageBox.Show(legacyInstance
                            ? "An older SuperPuTTY instance is running. Close it before starting this version in single-instance mode."
                            : "Unable to contact the running SuperPuTTY instance. Wait for it to finish starting or closing, then try again.",
                            "SuperPuTTY", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        Environment.ExitCode = 1;
                    }
                    return;
                }

                // open full file
                log4net.Config.XmlConfigurator.Configure();

                Log.Info("Starting");
                SuperPuTTY.Initialize(args);

                AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
                Application.ThreadException += new ThreadExceptionEventHandler(Application_ThreadException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var mainForm = new frmSuperPutty())
                {
                    SetDefaultIcon(mainForm);
                    if (instance?.IsPrimary == true)
                    {
                        listener = StartSingleInstanceComponent(singleInstance, () => SingleInstanceHelper.StartReceiving(mainForm, endpoint));
                        if (listener == null) { instance.Dispose(); instance = null; }
                    }
                    Application.Run(SuperPuTTY.MainForm = mainForm);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error in Main", ex);
                Environment.ExitCode = 1;
                if (ex is System.Configuration.ConfigurationErrorsException)
                    MessageBox.Show(ex.Message, "Unable to load preferences", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else if (ex is SingleInstanceStartupException)
                    MessageBox.Show(ex.Message, "Unable to start single-instance mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                try
                {
                    listener?.Dispose();
                    SuperPuTTY.Shutdown();
                    Log.Info("Shutdown");
                }
                finally { instance?.Dispose(); }
            }
        }

        internal sealed class SingleInstanceStartupException : IOException
        {
            internal SingleInstanceStartupException(Exception cause) : base(
                "SuperPuTTY could not start its single-instance connection. Close other SuperPuTTY windows and retry. " +
                "If it still fails, check Windows access to local named pipes and mutexes.\n\n" + cause.Message, cause) { }
        }

        internal static T StartSingleInstanceComponent<T>(bool required, Func<T> start) where T : class
        {
            try { return start(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is System.ComponentModel.Win32Exception || ex is System.Security.SecurityException ||
                ex is WaitHandleCannotBeOpenedException)
            {
                if (required) throw new SingleInstanceStartupException(ex);
                Log.Warn("Single-instance IPC is unavailable. Starting an independent SuperPuTTY window.", ex);
                return null;
            }
        }

        static void SetDefaultIcon(Form form)
        {
            // pure evil
            try
            {
                string iconFile = Path.Combine(Application.StartupPath, "SuperPutty.ico");
                if (File.Exists(iconFile))
                {
                    form.Icon = new Icon(iconFile);
                }

                FieldInfo field = typeof(Form).GetField("defaultIcon", BindingFlags.NonPublic | BindingFlags.Static);
                field.SetValue(null, form.Icon);
            }
            catch (Exception ex)
            {
                Log.WarnFormat("Could not set default icon: error={0}", ex.Message);
            }
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            String msg = String.Format("CurrentDomain_UnhandledException: IsTerminating={0}, ex={1}", e.IsTerminating, e.ExceptionObject);
            MessageBox.Show(msg, "Unhandled Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log.Error(msg);
        }

        /// <summary>
        /// For issue 218:
        /// http://stackoverflow.com/questions/295161/how-to-avoid-filenotfoundexception-if-net-3-5-is-not-installed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            StringBuilder sb = new StringBuilder();
            if (e.Exception.Message.Contains("Could not load file or assembly 'System.Core, Version=3.5.0.0"))
            {
                sb.Append("SuperPutty requires the Microsoft .NET Framework version 3.5, or greater, in order to run.\n\nPlease contact your System Administrator for more information.");
            }
            else
            {
                sb.Append(e.Exception);
            }

            Log.Error("Application_ThreadException", e.Exception);
            MessageBox.Show(sb.ToString(), "Application_ThreadException", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }



    }
}
