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
using System.Globalization;
using System.Windows.Forms;
using SuperPutty.Utils;

namespace SuperPuTTY.Scripting
{
    public static partial class Commands
    {
        /// <summary>Holds the Key and associate Key entry</summary>
        private class KeywordVk
        {
            internal string keyword;
            internal int vk;

            public KeywordVk(string key, int v)
            {
                keyword = key;
                vk = v;
            }
        }

        private static KeywordVk[] keywords = new KeywordVk[]
        {
            new KeywordVk("ENTER", (int)Keys.Return),
            new KeywordVk("TAB",         (int)Keys.Tab),
            new KeywordVk("ESC",         (int)Keys.Escape),
            new KeywordVk("ESCAPE",      (int)Keys.Escape),
            new KeywordVk("HOME",        (int)Keys.Home),
            new KeywordVk("END",         (int)Keys.End),
            new KeywordVk("LEFT",        (int)Keys.Left),
            new KeywordVk("RIGHT",       (int)Keys.Right),
            new KeywordVk("UP",          (int)Keys.Up),
            new KeywordVk("DOWN",        (int)Keys.Down),
            new KeywordVk("PGUP",        (int)Keys.Prior),
            new KeywordVk("PGDN",        (int)Keys.Next),
            new KeywordVk("NUMLOCK",     (int)Keys.NumLock),
            new KeywordVk("SCROLLLOCK",  (int)Keys.Scroll),
            new KeywordVk("PRTSC",       (int)Keys.PrintScreen),
            new KeywordVk("BREAK",       (int)Keys.Cancel),
            new KeywordVk("BACKSPACE",   (int)Keys.Back),
            new KeywordVk("BKSP",        (int)Keys.Back),
            new KeywordVk("BS",          (int)Keys.Back),
            new KeywordVk("CLEAR",       (int)Keys.Clear),
            new KeywordVk("CAPSLOCK",    (int)Keys.Capital),
            new KeywordVk("INS",         (int)Keys.Insert),
            new KeywordVk("INSERT",      (int)Keys.Insert),
            new KeywordVk("DEL",         (int)Keys.Delete),
            new KeywordVk("DELETE",      (int)Keys.Delete),
            new KeywordVk("HELP",        (int)Keys.Help),
            new KeywordVk("F1",          (int)Keys.F1),
            new KeywordVk("F2",          (int)Keys.F2),
            new KeywordVk("F3",          (int)Keys.F3),
            new KeywordVk("F4",          (int)Keys.F4),
            new KeywordVk("F5",          (int)Keys.F5),
            new KeywordVk("F6",          (int)Keys.F6),
            new KeywordVk("F7",          (int)Keys.F7),
            new KeywordVk("F8",          (int)Keys.F8),
            new KeywordVk("F9",          (int)Keys.F9),
            new KeywordVk("F10",         (int)Keys.F10),
            new KeywordVk("F11",         (int)Keys.F11),
            new KeywordVk("F12",         (int)Keys.F12),
            new KeywordVk("F13",         (int)Keys.F13),
            new KeywordVk("F14",         (int)Keys.F14),
            new KeywordVk("F15",         (int)Keys.F15),
            new KeywordVk("F16",         (int)Keys.F16),
            new KeywordVk("MULTIPLY",    (int)Keys.Multiply),
            new KeywordVk("ADD",         (int)Keys.Add),
            new KeywordVk("SUBTRACT",    (int)Keys.Subtract),
            new KeywordVk("DIVIDE",      (int)Keys.Divide),
            new KeywordVk("+",           (int)Keys.Add),
            new KeywordVk("%",           (int)(Keys.D5 | Keys.Shift)),
            new KeywordVk("^",           (int)(Keys.D6 | Keys.Shift))
        };
                
        /// <summary>Parse one key, optional modifiers, and an optional named-key repeat count.</summary>
        internal static CommandData SendKeyHandler(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) throw new ArgumentException("SENDKEY requires a key.");
            int index = 0;
            Keys modifiers = Keys.None;
            while (index < arg.Length && (arg[index] == '^' || arg[index] == '+' || arg[index] == '%'))
            {
                Keys modifier = arg[index] == '^' ? Keys.Control : arg[index] == '+' ? Keys.Shift : Keys.Alt;
                if ((modifiers & modifier) != 0) throw new ArgumentException("Duplicate key modifier.");
                modifiers |= modifier;
                index++;
            }
            string keyText = arg.Substring(index);
            int repeat = 1;
            int key;
            if (keyText.StartsWith("{"))
            {
                if (!keyText.EndsWith("}")) throw new ArgumentException("Unclosed named key.");
                string[] parts = keyText.Substring(1, keyText.Length - 2)
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 1 || parts.Length > 2) throw new ArgumentException("Invalid named key.");
                key = MatchKeyword(parts[0]);
                if (key < 0) throw new ArgumentException("Unknown named key.");
                if (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None,
                    CultureInfo.InvariantCulture, out repeat) || repeat < 1 || repeat > 10000))
                    throw new ArgumentException("Key repeat count must be between 1 and 10000.");
            }
            else
            {
                // Literal text belongs in SENDCHAR. Restrict bare key names to letters/digits.
                if (keyText.Length != 1 || !((keyText[0] >= 'a' && keyText[0] <= 'z')
                    || (keyText[0] >= 'A' && keyText[0] <= 'Z') || (keyText[0] >= '0' && keyText[0] <= '9')))
                    throw new ArgumentException("SENDKEY expects one letter, digit, or named key; use SENDCHAR for text.");
                key = char.ToUpperInvariant(keyText[0]);
            }
            return new CommandData(new KeyEventArgs((Keys)key | modifiers), repeat);
        }

        /// <summary>given a string, match the keyword to a key.</summary>
        /// <param name="keyword">The Keyword to search</param>
        /// <returns>An Integer corresponding to the matched keyword, or -1 if none exists</returns>
        private static int MatchKeyword(string keyword)
        {
            foreach (KeywordVk t in keywords)
            {
                if (String.Equals(t.keyword, keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return t.vk;
                }
            }
            return -1;
        }
    }
}
