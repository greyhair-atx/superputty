/*
 * Copyright (c) 2009 - 2015 Jim Radford http://www.jimradford.com
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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace SuperPutty.Data
{
    /// <summary>Contains parsed sessions and all validation errors from a CSV file.</summary>
    public sealed class SessionCsvImportResult
    {
        public SessionCsvImportResult()
        {
            Sessions = new List<SessionData>();
            Errors = new List<string>();
        }

        public List<SessionData> Sessions { get; private set; }

        public List<string> Errors { get; private set; }

        public bool IsValid
        {
            get { return Errors.Count == 0; }
        }
    }

    /// <summary>Reads and validates SuperPuTTY sessions stored in CSV format.</summary>
    public static class SessionCsvImporter
    {
        private static readonly string[] SupportedColumns =
        {
            "SessionName",
            "Host",
            "Protocol",
            "Port",
            "Username",
            "Folder",
            "PuttySession",
            "ExtraArgs",
            "Note",
            "ImageKey",
            "SPSLFileName",
            "RemotePath",
            "LocalPath"
        };

        /// <summary>
        /// Parses and validates the entire CSV file. No application sessions are modified by this method.
        /// Lines whose first character is '#' are treated as comments.
        /// </summary>
        public static SessionCsvImportResult Load(string fileName)
        {
            if (String.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("A CSV file name must be provided.", "fileName");
            }

            SessionCsvImportResult result = new SessionCsvImportResult();
            using (TextFieldParser parser = new TextFieldParser(fileName, Encoding.UTF8, true))
            {
                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(",");
                parser.HasFieldsEnclosedInQuotes = true;
                parser.TrimWhiteSpace = true;
                parser.CommentTokens = new[] { "#" };

                if (parser.EndOfData)
                {
                    result.Errors.Add("The CSV file is empty or contains only comments.");
                    return result;
                }

                string[] headers;
                try
                {
                    headers = parser.ReadFields();
                }
                catch (MalformedLineException ex)
                {
                    result.Errors.Add(FormatMalformedRow(parser, ex));
                    return result;
                }

                Dictionary<string, int> columns = ValidateHeaders(headers, result.Errors);
                if (result.Errors.Count > 0)
                {
                    return result;
                }

                HashSet<string> sessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int recordNumber = 1;
                while (!parser.EndOfData)
                {
                    recordNumber++;
                    string[] fields;
                    try
                    {
                        fields = parser.ReadFields();
                    }
                    catch (MalformedLineException ex)
                    {
                        result.Errors.Add(FormatMalformedRow(parser, ex));
                        continue;
                    }

                    if (fields.Length != headers.Length)
                    {
                        result.Errors.Add(String.Format(
                            "Row {0}: expected {1} fields but found {2}.",
                            recordNumber,
                            headers.Length,
                            fields.Length));
                        continue;
                    }

                    SessionData session = ParseRow(fields, columns, recordNumber, result.Errors);
                    if (session == null)
                    {
                        continue;
                    }

                    if (!sessionIds.Add(session.SessionId))
                    {
                        result.Errors.Add(String.Format(
                            "Row {0}: duplicate session path '{1}' also appears elsewhere in the CSV file.",
                            recordNumber,
                            session.SessionId));
                        continue;
                    }

                    result.Sessions.Add(session);
                }
            }

            if (result.Errors.Count > 0)
            {
                result.Sessions.Clear();
            }
            else if (result.Sessions.Count == 0)
            {
                result.Errors.Add("The CSV file does not contain any session rows.");
            }

            return result;
        }

        private static Dictionary<string, int> ValidateHeaders(string[] headers, List<string> errors)
        {
            Dictionary<string, int> columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> supported = new HashSet<string>(SupportedColumns, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < headers.Length; i++)
            {
                string header = (headers[i] ?? String.Empty).Trim();
                if (header.Length == 0)
                {
                    errors.Add(String.Format("Header column {0} is empty.", i + 1));
                }
                else if (!supported.Contains(header))
                {
                    errors.Add(String.Format(
                        "Header column {0}: unsupported column '{1}'. Supported columns are: {2}.",
                        i + 1,
                        header,
                        String.Join(", ", SupportedColumns)));
                }
                else if (columns.ContainsKey(header))
                {
                    errors.Add(String.Format("Header column {0}: duplicate column '{1}'.", i + 1, header));
                }
                else
                {
                    columns.Add(header, i);
                }
            }

            if (!columns.ContainsKey("SessionName"))
            {
                errors.Add("The required 'SessionName' column is missing.");
            }

            return columns;
        }

        private static SessionData ParseRow(
            string[] fields,
            Dictionary<string, int> columns,
            int recordNumber,
            List<string> errors)
        {
            int errorCount = errors.Count;
            string sessionName = GetValue(fields, columns, "SessionName");
            string host = GetValue(fields, columns, "Host");
            string puttySession = GetValue(fields, columns, "PuttySession");
            string folder = NormalizeFolder(GetValue(fields, columns, "Folder"));

            if (String.IsNullOrWhiteSpace(sessionName))
            {
                errors.Add(String.Format("Row {0}: SessionName is required.", recordNumber));
            }
            else if (sessionName.IndexOf('/') >= 0)
            {
                errors.Add(String.Format("Row {0}: SessionName cannot contain '/'. Use Folder for session folders.", recordNumber));
            }

            if (String.IsNullOrWhiteSpace(host) && String.IsNullOrWhiteSpace(puttySession))
            {
                errors.Add(String.Format("Row {0}: either Host or PuttySession is required.", recordNumber));
            }

            if (folder.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                errors.Add(String.Format("Row {0}: Folder contains an empty path segment.", recordNumber));
            }

            ConnectionProtocol protocol;
            string protocolValue = GetValue(fields, columns, "Protocol");
            if (!TryParseProtocol(protocolValue, out protocol))
            {
                errors.Add(String.Format("Row {0}: unsupported Protocol '{1}'.", recordNumber, protocolValue));
            }

            int port = GetDefaultPort(protocol);
            string portValue = GetValue(fields, columns, "Port");
            if (portValue.Length > 0 && (!Int32.TryParse(portValue, out port) || port < 0 || port > 65535))
            {
                errors.Add(String.Format("Row {0}: Port must be a number from 0 through 65535.", recordNumber));
            }

            if (errors.Count != errorCount)
            {
                return null;
            }

            if (puttySession.Length == 0)
            {
                puttySession = PuttyDataHelper.SessionDefaultSettings;
            }

            string sessionId = SessionData.CombineSessionIds(folder.Length == 0 ? null : folder, sessionName);
            return new SessionData
            {
                SessionId = sessionId,
                SessionName = sessionName,
                Host = host,
                Port = port,
                Proto = protocol,
                PuttySession = puttySession,
                Username = GetValue(fields, columns, "Username"),
                ExtraArgs = GetValue(fields, columns, "ExtraArgs"),
                Note = GetValue(fields, columns, "Note"),
                ImageKey = GetValue(fields, columns, "ImageKey"),
                SPSLFileName = GetValue(fields, columns, "SPSLFileName"),
                RemotePath = GetValue(fields, columns, "RemotePath"),
                LocalPath = GetValue(fields, columns, "LocalPath")
            };
        }

        private static string GetValue(string[] fields, Dictionary<string, int> columns, string column)
        {
            int index;
            return columns.TryGetValue(column, out index) ? (fields[index] ?? String.Empty).Trim() : String.Empty;
        }

        private static string NormalizeFolder(string folder)
        {
            return (folder ?? String.Empty).Trim().Replace('\\', '/').Trim('/');
        }

        private static bool TryParseProtocol(string value, out ConnectionProtocol protocol)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                protocol = ConnectionProtocol.SSH;
                return true;
            }

            string normalized = new string(value.Where(Char.IsLetterOrDigit).ToArray());
            if (normalized.Equals("PowerShell", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "PS";
            }
            else if (normalized.Equals("WinCommandPrompt", StringComparison.OrdinalIgnoreCase) ||
                     normalized.Equals("WindowsCommandPrompt", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "WINCMD";
            }

            return Enum.TryParse(normalized, true, out protocol) && Enum.IsDefined(typeof(ConnectionProtocol), protocol);
        }

        private static int GetDefaultPort(ConnectionProtocol protocol)
        {
            switch (protocol)
            {
                case ConnectionProtocol.Rlogin:
                    return 513;
                case ConnectionProtocol.Telnet:
                    return 23;
                case ConnectionProtocol.VNC:
                    return 5900;
                case ConnectionProtocol.RDP:
                    return 3389;
                case ConnectionProtocol.WINCMD:
                case ConnectionProtocol.PS:
                    return 0;
                default:
                    return 22;
            }
        }

        private static string FormatMalformedRow(TextFieldParser parser, MalformedLineException ex)
        {
            long rowNumber = parser.ErrorLineNumber;
            return String.Format(
                "Row {0}: malformed CSV data ({1}).",
                rowNumber > 0 ? rowNumber.ToString() : "unknown",
                ex.Message);
        }
    }
}
