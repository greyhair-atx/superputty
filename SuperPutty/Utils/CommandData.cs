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
        public int RepeatCount { get; private set; } = 1;

        /// <summary>Construct a new <seealso cref="CommandData"/> object, specifying a command to send</summary>
        /// <param name="command">A string containing the command to send</param>
        public CommandData(string command)
        {
            this.Command = command;
        }

        /// <summary>Construct a new <seealso cref="CommandData"/> object, specifying keyboard keystrokes to send</summary>
        /// <param name="keys">A <seealso cref="KeyEventArgs"/> object containing the keyboard keystrokes</param>
        public CommandData(KeyEventArgs keys) : this(keys, 1) { }

        public CommandData(KeyEventArgs keys, int repeatCount)
        {
            if (repeatCount < 1 || repeatCount > 10000) throw new ArgumentOutOfRangeException(nameof(repeatCount));
            RepeatCount = repeatCount;
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
            Action<int, int, int> send = (message, key, flags) =>
            {
                if (!isAlive()) return;
                UIntPtr result;
                // All input is acknowledged before sending the next message. Never mix queued
                // keystrokes with synchronous characters, which can overtake queued input.
                if (NativeMethods.SendMessageTimeout(handle, (uint)message, new IntPtr(key),
                    new IntPtr(flags), 0x23, 250, out result) == IntPtr.Zero && isAlive())
                    throw new InvalidOperationException("Terminal did not accept input in time.");
            };
            if (!string.IsNullOrEmpty(Command))
                foreach (char c in Command)
                {
                    if (!isAlive()) return;
                    send(NativeMethods.WM_CHAR, c, 0);
                }
            if (KeyData != null)
                for (int repeat = 0; repeat < RepeatCount; repeat++)
                {
                    if (!isAlive()) return;
                    SendKeys(KeyData, send);
                }
            if (Delay > TimeSpan.Zero) global::SuperPuTTY.Scripting.SPSL.Wait((int)Delay.TotalMilliseconds);
        }

        internal static void SendKeys(KeyEventArgs keys, Action<int, int, int> send)
        {
            int character;
            if (TryGetTerminalCharacter(keys, out character))
            {
                // Terminal Alt characters use the conventional ESC prefix. No global input,
                // keyboard-state manipulation, or focus changes are required.
                if (keys.Alt) send(NativeMethods.WM_CHAR, 27, 0);
                send(NativeMethods.WM_CHAR, character, 0);
                return;
            }
            if (keys.Modifiers != Keys.None)
                throw new NotSupportedException("This modified special key cannot be sent reliably to a background terminal.");

            int scan = (int)NativeMethods.MapVirtualKey((uint)keys.KeyCode, 0);
            int flags = 1 | (scan << 16);
            Keys key = keys.KeyCode;
            if (key == Keys.Insert || key == Keys.Delete || key == Keys.Home || key == Keys.End
                || key == Keys.Prior || key == Keys.Next || key == Keys.Left || key == Keys.Right
                || key == Keys.Up || key == Keys.Down || key == Keys.Divide || key == Keys.NumLock)
                flags |= 1 << 24;
            bool system = key == Keys.F10;
            send(system ? NativeMethods.WM_SYSKEYDOWN : NativeMethods.WM_KEYDOWN, (int)key, flags);
            send(system ? NativeMethods.WM_SYSKEYUP : NativeMethods.WM_KEYUP, (int)key, flags | unchecked((int)0xc0000000));
        }

        private static bool TryGetTerminalCharacter(KeyEventArgs keys, out int character)
        {
            character = 0;
            Keys key = keys.KeyCode;
            if (key >= Keys.A && key <= Keys.Z)
            {
                character = keys.Control ? (int)key - (int)Keys.A + 1
                    : (keys.Shift ? 'A' : 'a') + (int)key - (int)Keys.A;
                return true;
            }
            if (keys.Control) return false;
            if (key >= Keys.D0 && key <= Keys.D9)
            {
                character = keys.Shift ? ")!@#$%^&*("[(int)key - (int)Keys.D0] : '0' + (int)key - (int)Keys.D0;
                return true;
            }
            // These named character keys have no layout-dependent virtual-key translation.
            if (!keys.Shift && (key == Keys.Add || key == Keys.Subtract || key == Keys.Multiply || key == Keys.Divide))
            {
                character = key == Keys.Add ? '+' : key == Keys.Subtract ? '-' : key == Keys.Multiply ? '*' : '/';
                return true;
            }
            return false;
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
