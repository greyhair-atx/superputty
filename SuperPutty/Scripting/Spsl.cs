/*
 * https://github.com/jimradford/superputty/blob/master/License.txt
 */

using System;
using System.Linq;
using System.Threading;
using log4net;
using SuperPutty;
using SuperPutty.Utils;

namespace SuperPuTTY.Scripting
{
    /// <summary>SuperPuTTY Scripting Language</summary>
    public class SPSL
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SPSL));

        [ThreadStatic]
        private static ScriptTarget[] currentTargets;

        internal sealed class ScriptTarget
        {
            private readonly IntPtr handle;
            private readonly uint processId;
            private readonly uint threadId;
            private readonly Func<bool> sessionAlive;

            internal ScriptTarget(IntPtr handle, Func<bool> sessionAlive = null)
            {
                this.handle = handle;
                this.sessionAlive = sessionAlive;
                threadId = NativeMethods.GetWindowThreadProcessId(handle, out processId);
            }

            internal static ScriptTarget ForPanel(ApplicationPanel panel)
            {
                return new ScriptTarget(panel.AppWindowHandle, () => !panel.ScriptsStopped);
            }

            internal bool IsAlive
            {
                get
                {
                    uint owner;
                    return (sessionAlive == null || sessionAlive()) && threadId != 0
                        && NativeMethods.IsWindow(handle)
                        && NativeMethods.GetWindowThreadProcessId(handle, out owner) == threadId
                        && owner == processId;
                }
            }

            internal void Send(CommandData command)
            {
                if (IsAlive) command.SendToTerminalChecked(handle, () => IsAlive);
            }
        }

        internal static void CheckCancellation()
        {
            if (currentTargets != null && currentTargets.Length > 0 && !currentTargets.Any(target => target.IsAlive))
                throw new OperationCanceledException();
        }

        internal static void Wait(int milliseconds)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                CheckCancellation();
                int remaining = milliseconds - (int)elapsed.ElapsedMilliseconds;
                if (remaining <= 0) return;
                Thread.Sleep(Math.Min(remaining, 50));
            } while (true);
        }

        /// <summary>Holds the Key and associate Key entry</summary>
        private class SPSLFunction
        {
            internal string command;
            internal Func<string, CommandData> function;

            public SPSLFunction(string cmd, Func<string, CommandData> func)
            {
                command = cmd;
                function = func;
            }
        }

        /// <summary>Available SPSL Functions and their associated handlers</summary>
        private static SPSLFunction[] keywords = new SPSLFunction[]
        {
            new SPSLFunction("SENDKEY", Commands.SendKeyHandler),
            new SPSLFunction("OPENSESSION", Commands.OpenSessionHandler),
            new SPSLFunction("CLOSESESSION", Commands.CloseSessionHandler),
            new SPSLFunction("SENDCHAR", Commands.SendCharHandler),
            new SPSLFunction("SENDLINE", Commands.SendLineHandler),
            new SPSLFunction("SLEEP", Commands.SleepHandler),
            new SPSLFunction("PROMPT", Commands.PromptHandler),
            new SPSLFunction("PWDPROMPT", Commands.PrivatePromptHandler)
        };

        /// <summary>Try to parse a script line into commands and arguments</summary>
        /// <param name="line">The line to parse</param>
        /// <param name="commandData">A <seealso cref="CommandData"/> object with commands and keystrokes</param>
        /// <returns>true on success, false on failure, CommandData will be null for commands not requiring data to be sent</returns>        
        public static bool TryParseScriptLine(String line, out CommandData commandData)
        {
            commandData = null;
            line = line == null ? null : line.TrimStart();
            if (string.IsNullOrEmpty(line)
                || line.StartsWith("#")) // a comment line, ignore
            {
                return false;
            }


            string command = string.Empty;
            string args = string.Empty;

            int index = line.IndexOfAny(new[] { ' ', '\t' });
            if (index > 0)
            {
                command = line.Substring(0, index);
                args = line.Substring(index + 1).TrimEnd();
            }
            else
            {
                command = line.ToUpperInvariant().TrimEnd();
            }

            if (currentTargets != null && currentTargets.Length == 0
                && (command.Equals("SENDKEY", StringComparison.OrdinalIgnoreCase)
                    || command.Equals("SENDCHAR", StringComparison.OrdinalIgnoreCase)
                    || command.Equals("SENDLINE", StringComparison.OrdinalIgnoreCase)
                    || command.Equals("PROMPT", StringComparison.OrdinalIgnoreCase)
                    || command.Equals("PWDPROMPT", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Select a terminal before sending script input.");

            // lookup command and execute action associated with it.                
            Func<String, CommandData> spslCommand = MatchCommand(command);
            if (spslCommand != null)
            {
                commandData = spslCommand(args);
                return true;
            }
            else
            {
                throw new NotSupportedException("Unsupported SPSL command.");
            }
        }

        /// <summary>Execute a SPSL script Async</summary>
        /// <param name="scriptArgs">A <seealso cref="ExecuteScriptEventArgs"/> object containing the script to execute and parameters</param>
        public static void BeginExecuteScript(ExecuteScriptEventArgs scriptArgs)
        {
            string[] scriptlines = scriptArgs.Script.Split('\n');
            if (scriptlines.Length > 0
                && scriptArgs.IsSPSL)
            {
                CreateExecutionThread(scriptArgs, scriptlines).Start();
            }
        }

        internal static Thread CreateExecutionThread(ExecuteScriptEventArgs scriptArgs, string[] scriptlines, Action<int> onError = null)
        {
            var targets = scriptArgs.Targets ?? new[] { new ScriptTarget(scriptArgs.Handle) };
            var worker = new Thread(delegate ()
            {
                currentTargets = targets;
                for (int index = 0; index < scriptlines.Length; index++)
                {
                    try
                    {
                        CheckCancellation();
                        CommandData command;
                        TryParseScriptLine(scriptlines[index], out command);
                        CheckCancellation();
                        if (command != null)
                            foreach (var target in targets) target.Send(command);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        // Script arguments can contain credentials. Do not log the line or exception message.
                        int lineNumber = index + 1;
                        Log.WarnFormat("SPSL stopped at line {0}: {1}", lineNumber, ex.GetType().Name);
                        if (onError != null)
                            onError(lineNumber);
                        else
                            ReportScriptError(lineNumber, ex is NotSupportedException);
                        return;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "SPSL script execution"
            };
            worker.SetApartmentState(ApartmentState.STA);
            return worker;
        }

        private static void ReportScriptError(int lineNumber, bool unsupported)
        {
            var form = SuperPutty.SuperPuTTY.MainForm;
            if (form == null || form.IsDisposed || !form.IsHandleCreated)
                return;
            try
            {
                form.BeginInvoke(new Action(() =>
                {
                    if (!form.IsDisposed)
                        SuperPutty.SuperPuTTY.ReportStatus("Script stopped at line {0}: {1}", lineNumber,
                            unsupported ? "command or key combination is not supported" : "command failed; check its arguments and target session");
                }));
            }
            catch (InvalidOperationException) { } // The application closed while reporting the error.
        }

        /// <summary>Find Valid spsl script commands from lookup table and retrieve the Function to execute</summary>
        /// <param name="command">the SPSL command to lookup</param>
        /// <returns>The Function associated with the command or null of the command is invalid</returns>
        private static Func<string, CommandData> MatchCommand(string command)
        {
            return (from t 
                    in keywords
                    where String.Equals(t.command, command, StringComparison.OrdinalIgnoreCase)
                    select t.function).FirstOrDefault();
        }
    }
}
