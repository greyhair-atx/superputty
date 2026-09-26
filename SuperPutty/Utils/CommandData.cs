/*
 * Copyright (c) 2009 - 2015 Jim Radford http://www.jimradford.com
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"}, to deal
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
using System.Text;
using System.Windows.Forms;

namespace SuperPutty.Utils
{
    /// <summary>Store and retrieve commands and keystrokes for sending to sessions</summary>
    public class CommandData
    {
        /// <summary>Get the command to send</summary>
        public string Command { get; private set; }
        /// <summary>Get the keystrokes to send</summary>
        public KeyEventArgs KeyData { get; private set; }

        public TimeSpan Delay { get; private set; }

        /// <summary>Construct a new <seealso cref="CommandData"/> object, specifying a command to send</summary>
        /// <param name="command">A string containing the command to send</param>
        public CommandData(string command)
        {
            this.Command = command;
        }

        /// <summary>Construct a new <seealso cref="CommandData"/> object, specifying keyboard keystrokes to send</summary>
        /// <param name="keys">A <seealso cref="KeyEventArgs"/> object containing the keyboard keystrokes</param>
        public CommandData(KeyEventArgs keys)
        {
            this.KeyData = keys;
        }

        /// <summary>Construct a new <seealso cref="CommandData"/> object, specifying both a command and keyboard keystrokes to send</summary>
        /// /// <param name="command">A string containing the command to send</param>
        /// <param name="keys">A <seealso cref="KeyEventArgs"/> object containing the keyboard keystrokes</param>
        public CommandData(string command, KeyEventArgs keys)
        {
            this.Command = command;
            this.KeyData = keys;
        }

        /// <summary>Construct a new <seealso cref="CommandData"/> object, specifying both a command and keyboard keystrokes to send</summary>
        /// /// <param name="command">A string containing the command to send</param>
        /// <param name="keys">A <seealso cref="KeyEventArgs"/> object containing the keyboard keystrokes</param>
        /// <param name="delay">How long we should wait before executing next command</param>
        public CommandData(string command, KeyEventArgs keys, TimeSpan delay)
        {
            this.Command = command;
            this.KeyData = keys;
            this.Delay = delay;
        }

        /// <summary>Send commands and keystrokes to the specified session</summary>
        /// <param name="handle">The Windows Handle to send to</param>
        public void SendToTerminal(IntPtr handle)
        {
            var target = new global::SuperPuTTY.Scripting.SPSL.ScriptTarget(handle);
            target.Send(this);
        }

        internal void SendToTerminalChecked(IntPtr handle, Func<bool> isAlive)
        {
            // Keep keyboard messages targeted to this window, without changing global keyboard state.
            Action<int, int, int> post = (message, key, flags) =>
            {
                if (!isAlive()) return;
                if (!NativeMethods.PostMessage(handle, (uint)message, new IntPtr(key), new IntPtr(flags)))
                    throw new InvalidOperationException("Unable to queue terminal input.");
            };
            if (!string.IsNullOrEmpty(Command))
            {
                foreach (char c in Command)
                {
                    if (!isAlive()) return;
                    // Bound synchronous text delivery so hung terminals cannot strand a script.
                    UIntPtr result;
                    if (NativeMethods.SendMessageTimeout(handle, NativeMethods.WM_CHAR, new IntPtr(c),
                        IntPtr.Zero, 0x23, 250, out result) == IntPtr.Zero)
                    {
                        if (!isAlive()) return;
                        throw new InvalidOperationException("Terminal did not accept input in time.");
                    }
                }
            }
            if (KeyData != null) SendKeys(KeyData, post);
            if (Delay > TimeSpan.Zero) global::SuperPuTTY.Scripting.SPSL.Wait((int)Delay.TotalMilliseconds);
        }

        internal static void SendKeys(KeyEventArgs keys, Action<int, int, int> post)
        {
            Action<Keys, bool, bool> send = (key, up, alt) =>
            {
                int scan = (int)NativeMethods.MapVirtualKey((uint)key, 0);
                int flags = 1 | (scan << 16);
                if (key == Keys.Insert || key == Keys.Delete || key == Keys.Home || key == Keys.End
                    || key == Keys.Prior || key == Keys.Next || key == Keys.Left || key == Keys.Right
                    || key == Keys.Up || key == Keys.Down || key == Keys.Divide || key == Keys.NumLock)
                    flags |= 1 << 24;
                if (alt) flags |= 1 << 29;
                if (up) flags |= unchecked((int)0xc0000000);
                bool system = alt || key == Keys.Menu || key == Keys.F10;
                post(system ? (up ? NativeMethods.WM_SYSKEYUP : NativeMethods.WM_SYSKEYDOWN)
                    : (up ? NativeMethods.WM_KEYUP : NativeMethods.WM_KEYDOWN), (int)key, flags);
            };
            if (keys.Control) send(Keys.ControlKey, false, false);
            if (keys.Shift) send(Keys.ShiftKey, false, false);
            if (keys.Alt) send(Keys.Menu, false, false);
            send(keys.KeyCode, false, keys.Alt);
            send(keys.KeyCode, true, keys.Alt);
            if (keys.Alt) send(Keys.Menu, true, true);
            if (keys.Shift) send(Keys.ShiftKey, true, false);
            if (keys.Control) send(Keys.ControlKey, true, false);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(this.Command))
            {
                sb.Append(this.Command);
            }

            if (this.KeyData != null)
            {
                sb.AppendFormat("({0})", this.KeyData.KeyData);
            }
            return sb.ToString();
        }

    }
}
