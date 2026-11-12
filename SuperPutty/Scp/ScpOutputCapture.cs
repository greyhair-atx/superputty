using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SuperPutty.Scp
{
    internal sealed class ScpOutputLimitException : IOException
    {
        internal const string SafeMessage = "PSCP output exceeded the safety limit. Use a smaller directory or reduce verbose output and retry.";

        internal ScpOutputLimitException() : base(SafeMessage) { }
    }

    /// <summary>Preserves complete listings, or a bounded tail of transfer diagnostics.</summary>
    internal sealed class ScpOutputCapture
    {
        internal const int MaximumLineCharacters = 64 * 1024;
        internal const int MaximumListingCharacters = 8 * 1024 * 1024;
        internal const int MaximumListingLines = 100000;
        internal const int MaximumDiagnosticCharacters = 256 * 1024;
        internal const int MaximumDiagnosticLines = 1000;

        private readonly bool completeOutput;
        private readonly int maximumCharacters, maximumLines;
        private readonly Queue<string> lines = new Queue<string>();
        private int characters;

        internal ScpOutputCapture(bool completeOutput, int maximumCharacters = 0, int maximumLines = 0)
        {
            this.completeOutput = completeOutput;
            this.maximumCharacters = maximumCharacters > 0 ? maximumCharacters :
                (completeOutput ? MaximumListingCharacters : MaximumDiagnosticCharacters);
            this.maximumLines = maximumLines > 0 ? maximumLines :
                (completeOutput ? MaximumListingLines : MaximumDiagnosticLines);
        }

        internal void Add(string line)
        {
            if (line.Length > MaximumLineCharacters || line.Length + 1 > maximumCharacters)
                throw new ScpOutputLimitException();
            int cost = line.Length + 1; // Empty lines must consume the budget too.
            if (completeOutput && (lines.Count >= maximumLines || characters > maximumCharacters - cost))
                throw new ScpOutputLimitException();
            while (lines.Count >= maximumLines || characters > maximumCharacters - cost)
                characters -= lines.Dequeue().Length + 1;
            lines.Enqueue(line);
            characters += cost;
        }

        internal string[] ToArray() { return lines.ToArray(); }

        internal static string ReadLine(TextReader reader)
        {
            var line = new StringBuilder();
            int value;
            while ((value = reader.Read()) != -1)
            {
                char character = (char)value;
                if (character == '\r' || character == '\n')
                {
                    if (character == '\r' && reader.Peek() == '\n') reader.Read();
                    return line.ToString();
                }
                if (line.Length >= MaximumLineCharacters) throw new ScpOutputLimitException();
                line.Append(character);
            }
            return line.Length == 0 ? null : line.ToString();
        }

        /// <summary>Read BinaryWriter's UTF-8 framing without allocating an unchecked string.</summary>
        internal static string ReadString(BinaryReader reader)
        {
            uint length = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte next = reader.ReadByte();
                if (shift == 28 && (next & 0xf8) != 0) throw new ScpOutputLimitException();
                length |= (uint)(next & 0x7f) << shift;
                if ((next & 0x80) == 0)
                {
                    if (length > MaximumLineCharacters * 4) throw new ScpOutputLimitException();
                    byte[] bytes = reader.ReadBytes((int)length);
                    if (bytes.Length != length) throw new EndOfStreamException();
                    string value = Encoding.UTF8.GetString(bytes);
                    if (value.Length > MaximumLineCharacters) throw new ScpOutputLimitException();
                    return value;
                }
            }
            throw new ScpOutputLimitException();
        }
    }
}
