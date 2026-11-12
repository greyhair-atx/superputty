using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using log4net;

namespace SuperPutty.Data
{
    internal sealed class LayoutLoadResult
    {
        internal byte[] Content { get; private set; }
        internal string BackupFilePath { get; private set; }
        internal string PreservedFilePath { get; private set; }

        internal LayoutLoadResult(byte[] content, string backupFilePath = null, string preservedFilePath = null)
        {
            Content = content;
            BackupFilePath = backupFilePath;
            PreservedFilePath = preservedFilePath;
        }
    }

    /// <summary>Publishes validated layout files without truncating the previous save.</summary>
    internal static class LayoutStorage
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(LayoutStorage));
        internal const int MaximumLayoutBytes = 16 * 1024 * 1024;
        internal const int MaximumLayoutItems = 1024;

        private static byte[] ReadValidated(string path, Action<byte[]> validateLayout)
        {
            byte[] content;
            using (var input = File.OpenRead(path))
            using (var output = new MemoryStream())
            {
                if (input.Length > MaximumLayoutBytes) throw new InvalidDataException("The layout exceeds the 16 MiB limit.");
                var buffer = new byte[8192];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaximumLayoutBytes) throw new InvalidDataException("The layout exceeds the 16 MiB limit.");
                    output.Write(buffer, 0, read);
                }
                content = output.ToArray();
            }
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumLayoutBytes
                };
                var document = new XmlDocument { XmlResolver = null };
                using (var stream = new MemoryStream(content, false))
                using (var reader = XmlReader.Create(stream, settings)) document.Load(reader);
                if (document.DocumentElement == null || document.DocumentElement.Name != "DockPanel" ||
                    document.DocumentElement.NamespaceURI.Length != 0)
                    throw new InvalidDataException("The layout must contain a DockPanel root without a namespace.");
                // DockPanelSuite allocates arrays from Count before reading their
                // entries. Reject tiny files with huge or inconsistent declarations.
                foreach (XmlElement element in document.SelectNodes("//*[@Count]"))
                {
                    int declared;
                    int actual = element.ChildNodes.OfType<XmlElement>().Count();
                    if (!Int32.TryParse(element.GetAttribute("Count"), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out declared) || declared < 0 ||
                        declared > MaximumLayoutItems || declared != actual)
                        throw new InvalidDataException("A layout element count is invalid or exceeds the supported limit.");
                }
                if (document.SelectNodes("//*").Count > MaximumLayoutItems * 16)
                    throw new InvalidDataException("The layout contains too many elements.");

                var singletonNames = new HashSet<string>(StringComparer.Ordinal)
                {
                    typeof(SessionTreeview).FullName, typeof(LayoutsList).FullName,
                    typeof(Log4netLogViewer).FullName, typeof(SessionDetail).FullName
                };
                var seenSingletons = new HashSet<string>(StringComparer.Ordinal);
                int sessionCount = 0;
                foreach (XmlElement entry in document.SelectNodes("/DockPanel/Contents/Content"))
                {
                    string name = entry.GetAttribute("PersistString");
                    if (name == typeof(ctlPuttyPanel).FullName ||
                        name.StartsWith(typeof(ctlPuttyPanel).FullName + "?", StringComparison.Ordinal) ||
                        name.StartsWith(typeof(ctlPuttyPanel).FullName + ":", StringComparison.Ordinal) ||
                        name == typeof(Scp.PscpBrowserPanel).FullName ||
                        name.StartsWith(typeof(Scp.PscpBrowserPanel).FullName + "?", StringComparison.Ordinal))
                    {
                        if (++sessionCount > SuperPuTTY.MaximumOpenSessions)
                            throw new InvalidDataException("The layout exceeds the limit of 200 session tabs.");
                    }
                    if (singletonNames.Contains(name) && !seenSingletons.Add(name))
                        throw new InvalidDataException("A singleton tool window appears more than once in the layout.");
                }
                validateLayout(content);
                return content;
            }
            catch (Exception ex) when (ex is XmlException || ex is ArgumentException || ex is InvalidOperationException ||
                ex is FormatException || ex is OverflowException || ex is IndexOutOfRangeException || ex is NullReferenceException)
            {
                throw new InvalidDataException("The DockPanel layout is invalid or uses an unsupported format.", ex);
            }
        }

        internal static LayoutLoadResult LoadValidated(string filePath, Action<byte[]> validateLayout)
        {
            if (validateLayout == null) throw new ArgumentNullException(nameof(validateLayout));
            bool missing = false;
            Exception failure;
            try { return new LayoutLoadResult(ReadValidated(filePath, validateLayout)); }
            catch (FileNotFoundException ex) { missing = true; failure = ex; }
            catch (DirectoryNotFoundException ex) { missing = true; failure = ex; }
            catch (InvalidDataException ex) { failure = ex; }

            string backupPath = filePath + ".bak";
            byte[] backup;
            try { backup = ReadValidated(backupPath, validateLayout); }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is InvalidDataException)
            {
                throw new InvalidDataException("No valid layout or previous-save backup could be loaded: " + filePath,
                    new AggregateException(failure, ex));
            }

            string preserved = missing ? null : filePath + ".corrupt." + Guid.NewGuid().ToString("N");
            string temporary = TemporaryPath(filePath);
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(backup, 0, backup.Length);
                    stream.Flush(true);
                }
                if (missing) File.Move(temporary, filePath);
                else File.Replace(temporary, filePath, preserved);
            }
            finally { RemoveTemporary(temporary); }

            Log.WarnFormat("Recovered layout from {0}. Preserved original: {1}", backupPath, preserved ?? "primary was missing");
            return new LayoutLoadResult(backup, backupPath, preserved);
        }

        /// <returns>The preserved filename when an invalid existing layout was replaced.</returns>
        internal static string SaveAtomically(string filePath, Action<string> serialize, Action<byte[]> validateLayout)
        {
            if (serialize == null) throw new ArgumentNullException(nameof(serialize));
            if (validateLayout == null) throw new ArgumentNullException(nameof(validateLayout));
            string temporary = TemporaryPath(filePath);
            try
            {
                serialize(temporary);
                ReadValidated(temporary, validateLayout);
                using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Write, FileShare.None)) stream.Flush(true);

                bool missing = false;
                bool invalid = false;
                try { ReadValidated(filePath, validateLayout); }
                catch (FileNotFoundException) { missing = true; }
                catch (DirectoryNotFoundException) { missing = true; }
                catch (InvalidDataException) { invalid = true; }

                string preserved = invalid ? filePath + ".corrupt." + Guid.NewGuid().ToString("N") : null;
                if (missing) File.Move(temporary, filePath);
                else File.Replace(temporary, filePath, invalid ? preserved : filePath + ".bak");
                if (invalid) Log.WarnFormat("Preserved invalid layout at {0}; previous-save backup was not replaced.", preserved);
                return preserved;
            }
            finally { RemoveTemporary(temporary); }
        }

        internal static IEnumerable<string> Discover(string directory)
        {
            var files = Directory.GetFiles(directory);
            return files.Where(path => String.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase))
                .Concat(files.Where(path => path.EndsWith(".xml.bak", StringComparison.OrdinalIgnoreCase))
                    .Select(path => path.Substring(0, path.Length - ".bak".Length)))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static bool TryApply(string filePath, Action<byte[]> validateLayout, Action closeCurrent,
            Action<byte[]> applyLayout, Action restoreDefault, out LayoutLoadResult prepared,
            out Exception failure, out bool teardownStarted)
        {
            prepared = null;
            failure = null;
            teardownStarted = false;
            try { prepared = LoadValidated(filePath, validateLayout); }
            catch (Exception ex) when (IsLoadFailure(ex)) { failure = ex; return false; }

            try
            {
                teardownStarted = true;
                closeCurrent();
                applyLayout(prepared.Content);
                return true;
            }
            catch (Exception ex) when (IsLoadFailure(ex))
            {
                failure = ex;
                try { restoreDefault(); }
                catch (Exception fallbackFailure) when (IsLoadFailure(fallbackFailure))
                {
                    failure = new InvalidOperationException("The layout could not be loaded and the default workspace could not be fully restored.",
                        new AggregateException(ex, fallbackFailure));
                }
                return false;
            }
        }

        private static bool IsLoadFailure(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException ||
                ex is XmlException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException ||
                ex is OverflowException || ex is IndexOutOfRangeException || ex is NullReferenceException || ex is System.ComponentModel.Win32Exception;
        }

        internal static void MoveFiles(string oldPath, string newPath)
        {
            var moved = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (string suffix in new[] { String.Empty, ".bak" })
                {
                    string source = oldPath + suffix;
                    try { File.GetAttributes(source); }
                    catch (FileNotFoundException) { continue; }
                    catch (DirectoryNotFoundException) { continue; }
                    File.Move(source, newPath + suffix);
                    moved.Add(new KeyValuePair<string, string>(source, newPath + suffix));
                }
                if (moved.Count == 0) throw new FileNotFoundException("The layout and its backup no longer exist.", oldPath);
            }
            catch (Exception failure)
            {
                try
                {
                    foreach (var pair in moved.AsEnumerable().Reverse()) File.Move(pair.Value, pair.Key);
                }
                catch (Exception rollbackFailure)
                {
                    throw new IOException("The layout rename failed and could not be completely rolled back. Check both layout locations.",
                        new AggregateException(failure, rollbackFailure));
                }
                throw;
            }
        }

        private static string TemporaryPath(string path) { return path + "." + Guid.NewGuid().ToString("N") + ".tmp"; }

        private static void RemoveTemporary(string path)
        {
            try { File.Delete(path); }
            catch (IOException ex) { Log.Warn("Could not remove a temporary layout file", ex); }
            catch (UnauthorizedAccessException ex) { Log.Warn("Could not remove a temporary layout file", ex); }
        }
    }
}
