using System;
using System.Collections.Generic;
using System.Text;

namespace SuperPutty.Utils
{
    /// <summary>Windows argument decoding with source spans for edits that leave other arguments intact.</summary>
    internal static class WindowsCommandLine
    {
        internal sealed class Token
        {
            internal int Start { get; }
            internal int End { get; }
            internal string Value { get; }
            internal bool Complete { get; }

            internal Token(int start, int end, string value, bool complete)
            { Start = start; End = end; Value = value; Complete = complete; }
        }

        internal static List<Token> Parse(string arguments)
        {
            var tokens = new List<Token>();
            if (String.IsNullOrEmpty(arguments)) return tokens;
            int position = 0;
            while (position < arguments.Length)
            {
                while (position < arguments.Length && IsSeparator(arguments[position])) position++;
                if (position == arguments.Length) break;
                int start = position;
                int quotes = 0;
                var value = new StringBuilder();
                while (position < arguments.Length && (quotes != 0 || !IsSeparator(arguments[position])))
                {
                    int slashes = 0;
                    while (position < arguments.Length && arguments[position] == '\\')
                    { slashes++; position++; }
                    if (position < arguments.Length && arguments[position] == '"')
                    {
                        value.Append('\\', slashes / 2);
                        if ((slashes & 1) != 0)
                        {
                            value.Append('"');
                            position++;
                        }
                        else
                        {
                            quotes++;
                            position++;
                            while (position < arguments.Length && arguments[position] == '"')
                            {
                                if (++quotes == 3) { value.Append('"'); quotes = 0; }
                                position++;
                            }
                            if (quotes == 2) quotes = 0;
                        }
                    }
                    else
                    {
                        value.Append('\\', slashes);
                        if (position < arguments.Length && (quotes != 0 || !IsSeparator(arguments[position])))
                            value.Append(arguments[position++]);
                    }
                }
                tokens.Add(new Token(start, position, value.ToString(), quotes == 0));
            }
            return tokens;
        }

        private static bool IsSeparator(char character) { return character == ' ' || character == '\t'; }

        private static bool TryOption(string value, string option, out string attachedValue, out char separator)
        {
            attachedValue = null;
            separator = '\0';
            if (String.Equals(value, option, StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Length > option.Length && value.StartsWith(option, StringComparison.OrdinalIgnoreCase) &&
                (value[option.Length] == '=' || value[option.Length] == ':'))
            {
                separator = value[option.Length];
                attachedValue = value.Substring(option.Length + 1);
                return true;
            }
            return false;
        }

        private static readonly string[] PasswordOptions = { "-pw", "-password", "/password", "/p" };

        private static bool SensitiveOption(string value, bool includePasswordFile, out string option,
            out string attachedValue, out char separator)
        {
            foreach (string candidate in PasswordOptions)
                if (TryOption(value, candidate, out attachedValue, out separator))
                { option = value.Substring(0, candidate.Length); return true; }
            if (includePasswordFile && TryOption(value, "-pwfile", out attachedValue, out separator))
            { option = value.Substring(0, 7); return true; }
            option = attachedValue = null;
            separator = '\0';
            return false;
        }

        private static bool IsSensitiveOption(string value, bool includePasswordFile)
        {
            string option, attached;
            char separator;
            return SensitiveOption(value, includePasswordFile, out option, out attached, out separator);
        }

        internal static string GetOptionValue(string arguments, string option)
        {
            if (String.IsNullOrEmpty(option)) return String.Empty;
            var tokens = Parse(arguments);
            for (int i = 0; i < tokens.Count; i++)
            {
                string attached;
                char separator;
                Token token = tokens[i];
                if (!TryOption(token.Value, option, out attached, out separator) || !token.Complete) continue;
                if (attached != null) return attached;
                if (i + 1 < tokens.Count && tokens[i + 1].Complete)
                    return tokens[i + 1].Value;
            }
            return String.Empty;
        }

        internal static bool ContainsSensitiveOption(string arguments)
        {
            foreach (Token token in Parse(arguments))
                if (IsSensitiveOption(token.Value, false)) return true;
            return false;
        }

        /// <summary>Null replacement removes switches; other values replace only their credential payloads.</summary>
        internal static string RewriteSensitive(string arguments, string replacement,
            bool includePasswordFile = false, bool removeBatch = false)
        {
            if (String.IsNullOrEmpty(arguments)) return arguments ?? String.Empty;
            var tokens = Parse(arguments);
            var output = new StringBuilder();
            int copied = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                Token token = tokens[i];
                string option, attached;
                char separator;
                bool batch = removeBatch && String.Equals(token.Value, "-batch", StringComparison.OrdinalIgnoreCase);
                if (!SensitiveOption(token.Value, includePasswordFile, out option, out attached, out separator) && !batch)
                    continue;
                int end = token.End;
                if (!batch && token.Complete && attached == null && i + 1 < tokens.Count)
                    end = tokens[++i].End;
                output.Append(arguments, copied, token.Start - copied);
                if (!batch && replacement != null)
                {
                    if (separator != '\0') output.Append(option).Append(separator).Append(replacement);
                    else output.Append(token.Complete ? arguments.Substring(token.Start, token.End - token.Start) : option)
                        .Append(' ').Append(replacement);
                }
                copied = end;
            }
            if (copied == 0) return arguments;
            output.Append(arguments, copied, arguments.Length - copied);
            return replacement == null ? output.ToString().Trim() : output.ToString();
        }

        /// <summary>Arguments from Main/IPC are already decoded; each element stays indivisible.</summary>
        internal static IEnumerable<string> RedactDecoded(IEnumerable<string> arguments, string replacement)
        {
            bool redactNext = false;
            foreach (string argument in arguments)
            {
                string value = argument ?? String.Empty;
                if (redactNext)
                { yield return replacement; redactNext = false; continue; }
                redactNext = false;
                string option, attached;
                char separator;
                if (SensitiveOption(value, false, out option, out attached, out separator))
                {
                    if (attached != null) yield return option + separator + replacement;
                    else { yield return value; redactNext = true; }
                }
                else yield return value;
            }
        }
    }
}
