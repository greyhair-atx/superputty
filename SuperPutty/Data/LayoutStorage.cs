using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using log4net;
using SuperPutty.Utils;

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
            return ValidateContent(SharedFile.Read(path, MaximumLayoutBytes), validateLayout);
        }

        private static byte[] ReadValidated(string path, Action<byte[]> validateLayout, out SharedFile.FileVersion version,
            bool protectApplicationData = false)
        {
            return ValidateContent(SharedFile.Capture(path, MaximumLayoutBytes, out version), validateLayout, protectApplicationData);
        }

        private static byte[] ValidateContent(byte[] content, Action<byte[]> validateLayout, bool protectApplicationData = false)
        {
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumLayoutBytes
                };
                var document = new XmlDocument { XmlResolver = null };
                using (var stream = new MemoryStream(content, false))
                using (var reader = XmlReader.Create(stream, settings)) document.Load(reader);
                if (protectApplicationData && (document.DocumentElement?.Name == "ArrayOfSessionData" ||
                    document.DocumentElement?.Name == "Settings"))
                    throw new IOException("Choose a different layout filename. The selected file contains sessions or preferences.");
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
            using (SharedFile.Lock(filePath))
            {
                for (int attempt = 0; ; attempt++)
                {
                    try { return LoadValidatedCore(filePath, validateLayout); }
                    catch (SharedFile.ChangedException) when (attempt < 2) { }
                }
            }
        }

        private static LayoutLoadResult LoadValidatedCore(string filePath, Action<byte[]> validateLayout)
        {
            if (validateLayout == null) throw new ArgumentNullException(nameof(validateLayout));
            SharedFile.FileVersion version = new SharedFile.FileVersion { Missing = true };
            bool missing = false;
            Exception failure;
            try { return new LayoutLoadResult(ReadValidated(filePath, validateLayout, out version)); }
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
                SharedFile.PublishVersion(temporary, filePath, preserved, version);
            }
            finally { RemoveTemporary(temporary); }

            Log.WarnFormat("Recovered layout from {0}. Preserved original: {1}", backupPath, preserved ?? "primary was missing");
            return new LayoutLoadResult(backup, backupPath, preserved);
        }

        /// <returns>The preserved filename when an invalid existing layout was replaced.</returns>
        internal static string SaveAtomically(string filePath, Action<string> serialize, Action<byte[]> validateLayout,
            Action<string> validateDestination = null)
        {
            using (SharedFile.Lock(filePath))
            {
                validateDestination?.Invoke(filePath);
                return SaveAtomicallyCore(filePath, serialize, validateLayout, validateDestination);
            }
        }

        private static string SaveAtomicallyCore(string filePath, Action<string> serialize, Action<byte[]> validateLayout,
            Action<string> validateDestination)
        {
            if (serialize == null) throw new ArgumentNullException(nameof(serialize));
            if (validateLayout == null) throw new ArgumentNullException(nameof(validateLayout));
            SharedFile.FileVersion version = new SharedFile.FileVersion { Missing = true };
            bool invalid = false;
            try { ReadValidated(filePath, validateLayout, out version, protectApplicationData: true); }
            catch (FileNotFoundException) { version = new SharedFile.FileVersion { Missing = true }; }
            catch (DirectoryNotFoundException) { version = new SharedFile.FileVersion { Missing = true }; }
            catch (InvalidDataException) { invalid = true; }
            string backup = invalid ? filePath + ".corrupt." + Guid.NewGuid().ToString("N") :
                version.Missing ? null : filePath + ".bak";
            if (backup != null) validateDestination?.Invoke(backup);
            string temporary = TemporaryPath(filePath);
            try
            {
                serialize(temporary);
                ReadValidated(temporary, validateLayout);
                using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Write, FileShare.None)) stream.Flush(true);

                string preserved = invalid ? backup : null;
                SharedFile.PublishVersion(temporary, filePath, backup, version);
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
            // Every rename takes both locks in canonical order to avoid deadlock.
            var paths = new[] { Path.GetFullPath(oldPath), Path.GetFullPath(newPath) }.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            using (SharedFile.Lock(paths[0]))
            using (SharedFile.Lock(paths[1]))
                MoveFilesCore(oldPath, newPath);
        }

        private static void MoveFilesCore(string oldPath, string newPath)
        {
            // A layout's primary and backup form one destination. Check both names
            // even when the source has only one component, so a rename cannot
            // attach an unrelated existing backup or primary to the moved layout.
            foreach (string suffix in new[] { String.Empty, ".bak" })
            {
                string destination = newPath + suffix;
                try { File.GetAttributes(destination); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                throw new IOException("A layout or backup already exists at the destination: " + destination);
            }

            var moved = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (string suffix in new[] { String.Empty, ".bak" })
                {
                    string source = oldPath + suffix;
                    try { File.GetAttributes(source); }
                    catch (FileNotFoundException) { continue; }
                    catch (DirectoryNotFoundException) { continue; }
                    SharedFile.Retry(() => { File.Move(source, newPath + suffix); return true; });
                    moved.Add(new KeyValuePair<string, string>(source, newPath + suffix));
                }
                if (moved.Count == 0) throw new FileNotFoundException("The layout and its backup no longer exist.", oldPath);
            }
            catch (Exception failure)
            {
                try
                {
                    foreach (var pair in moved.AsEnumerable().Reverse())
                        SharedFile.Retry(() => { File.Move(pair.Value, pair.Key); return true; });
                }
                catch (Exception rollbackFailure)
                {
                    throw new IOException("The layout rename failed and could not be completely rolled back. Check both layout locations.",
                        new AggregateException(failure, rollbackFailure));
                }
                throw;
            }
        }

        internal static void DeleteFiles(string filePath)
        {
            using (SharedFile.Lock(filePath))
            {
                var staged = new List<KeyValuePair<string, string>>();
                try
                {
                    foreach (string suffix in new[] { String.Empty, ".bak" })
                    {
                        string source = filePath + suffix;
                        string removed = TemporaryPath(filePath) + ".deleted";
                        try { SharedFile.Retry(() => { File.Move(source, removed); return true; }); }
                        catch (FileNotFoundException) { continue; }
                        catch (DirectoryNotFoundException) { continue; }
                        staged.Add(new KeyValuePair<string, string>(source, removed));
                    }
                }
                catch (Exception failure)
                {
                    try
                    {
                        foreach (var pair in staged.AsEnumerable().Reverse())
                            SharedFile.Retry(() => { File.Move(pair.Value, pair.Key); return true; });
                    }
                    catch (Exception rollbackFailure)
                    {
                        throw new IOException("The layout could not be deleted or completely restored. Check the layout folder for preserved files.",
                            new AggregateException(failure, rollbackFailure));
                    }
                    throw;
                }

                // Both active names have been removed under the destination lock.
                // Cleanup failure must not turn a committed delete into a partial
                // restore. Any retained staging file is excluded from discovery.
                foreach (var pair in staged) RemoveTemporary(pair.Value);
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
