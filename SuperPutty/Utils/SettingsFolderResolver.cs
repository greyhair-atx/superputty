using System;
using System.IO;
using System.Security;
using SuperPutty.Data;

namespace SuperPutty.Utils
{
    internal static class SettingsFolderResolver
    {
        internal const string ApplicationFolderName = "SuperPuTTY";

        internal static string DefaultSettingsFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    ApplicationFolderName);
            }
        }

        internal static string LegacyDocumentsSettingsFolder
        {
            get
            {
                string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return String.IsNullOrWhiteSpace(documentsFolder)
                    ? null
                    : Path.Combine(documentsFolder, ApplicationFolderName);
            }
        }

        internal static string GetConfiguredOrLegacyFolder(string configuredFolder)
        {
            return GetConfiguredOrLegacyFolder(configuredFolder, LegacyDocumentsSettingsFolder);
        }

        internal static string GetConfiguredOrLegacyFolder(string configuredFolder, string legacyFolder)
        {
            if (!String.IsNullOrWhiteSpace(configuredFolder))
            {
                return configuredFolder;
            }

            return !String.IsNullOrWhiteSpace(legacyFolder) && Directory.Exists(legacyFolder)
                ? legacyFolder
                : null;
        }

        internal static string ResolveWritableFolder(
            string configuredFolder,
            out bool usedFallback,
            out Exception configuredFolderError)
        {
            return ResolveWritableFolder(
                configuredFolder,
                DefaultSettingsFolder,
                out usedFallback,
                out configuredFolderError);
        }

        internal static string ResolveWritableFolder(
            string configuredFolder,
            string fallbackFolder,
            out bool usedFallback,
            out Exception configuredFolderError)
        {
            usedFallback = false;
            configuredFolderError = null;

            string preferredFolder = String.IsNullOrWhiteSpace(configuredFolder)
                ? fallbackFolder
                : configuredFolder;

            if (TryEnsureWritable(preferredFolder, out configuredFolderError))
            {
                return preferredFolder;
            }

            if (PathsEqual(preferredFolder, fallbackFolder))
            {
                throw new IOException(
                    "The SuperPuTTY CE settings folder is not writable: " + fallbackFolder,
                    configuredFolderError);
            }

            Exception fallbackError;
            if (!TryEnsureWritable(fallbackFolder, out fallbackError))
            {
                throw new IOException(
                    "Neither the configured SuperPuTTY CE settings folder nor the Local AppData fallback is writable.",
                    new AggregateException(configuredFolderError, fallbackError));
            }

            usedFallback = true;
            return fallbackFolder;
        }

        internal static bool TryEnsureWritable(string folder, out Exception error)
        {
            error = null;
            string probePath = null;

            try
            {
                if (String.IsNullOrWhiteSpace(folder))
                {
                    throw new ArgumentException("A settings folder is required.", "folder");
                }

                Directory.CreateDirectory(folder);
                probePath = Path.Combine(folder, ".superputty-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream stream = new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough))
                {
                    stream.WriteByte(0);
                    stream.Flush(true);
                }
                File.Delete(probePath);
                return true;
            }
            catch (Exception ex)
            {
                if (!IsStorageException(ex))
                {
                    throw;
                }

                error = ex;
                if (!String.IsNullOrEmpty(probePath))
                {
                    try
                    {
                        if (File.Exists(probePath))
                        {
                            File.Delete(probePath);
                        }
                    }
                    catch (Exception cleanupException)
                    {
                        if (!IsStorageException(cleanupException))
                        {
                            throw;
                        }
                    }
                }
                return false;
            }
        }

        internal static void CopyExistingSettings(string sourceFolder, string destinationFolder, Action<string> validateCatalog = null)
        {
            if (String.IsNullOrWhiteSpace(destinationFolder) || PathsEqual(sourceFolder, destinationFolder))
            {
                return;
            }

            Directory.CreateDirectory(destinationFolder);
            // Check metadata explicitly. Directory.Exists hides access errors
            // that should keep the configured folder in place.
            bool sourceAvailable = !String.IsNullOrWhiteSpace(sourceFolder) && IsDirectory(sourceFolder);
            CopyIfMissing(sourceAvailable ? Path.Combine(sourceFolder, "Sessions.XML") : null,
                Path.Combine(destinationFolder, "Sessions.XML"), validateCatalog, SessionStorage.MaximumLocalCatalogBytes);
            if (!sourceAvailable) return;

            CopyIfMissing(Path.Combine(sourceFolder, "AutoRestoreLayout.XML"), Path.Combine(destinationFolder, "AutoRestoreLayout.XML"));

            string sourceLayouts = Path.Combine(sourceFolder, "layouts");
            if (!IsDirectory(sourceLayouts))
            {
                return;
            }

            string destinationLayouts = Path.Combine(destinationFolder, "layouts");
            Directory.CreateDirectory(destinationLayouts);
            foreach (string sourceFile in Directory.EnumerateFiles(sourceLayouts, "*.xml"))
                CopyIfMissing(sourceFile, Path.Combine(destinationLayouts, Path.GetFileName(sourceFile)));
        }

        private static FileAttributes? GetAttributesIfPresent(string path)
        {
            try { return File.GetAttributes(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        private static bool IsDirectory(string path)
        {
            var attributes = GetAttributesIfPresent(path);
            return attributes.HasValue && (attributes.Value & FileAttributes.Directory) != 0;
        }

        private static void CopyIfMissing(string source, string destination, Action<string> validate = null,
            long maximumBytes = Int64.MaxValue)
        {
            using (SharedFile.Lock(destination))
            {
                if (GetAttributesIfPresent(destination).HasValue)
                {
                    validate?.Invoke(destination);
                    return;
                }
                if (source == null) return;
                using (var input = SharedFile.OpenOptional(source))
                {
                    if (input == null) return;
                    if (input.Length > maximumBytes) throw new InvalidDataException("The session catalog exceeds its supported size limit.");
                    string staged = destination + ".migration." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            input.CopyTo(output);
                            output.Flush(true);
                        }
                        validate?.Invoke(staged);
                        try { SharedFile.Retry(() => { File.Move(staged, destination); return true; }); }
                        catch (IOException ex) when ((ex.HResult & 65535) == 80 || (ex.HResult & 65535) == 183)
                        {
                            // A nonparticipating writer created the destination.
                            // Keep its data and validate it before accepting the folder.
                            validate?.Invoke(destination);
                        }
                    }
                    finally
                    {
                        try { File.Delete(staged); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }
        }

        internal static bool PathsEqual(string left, string right)
        {
            try
            {
                if (String.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return true;
                return String.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                if (!IsStorageException(ex))
                {
                    throw;
                }
                return false;
            }
        }

        private static bool IsStorageException(Exception exception)
        {
            return exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is SecurityException ||
                exception is ArgumentException ||
                exception is NotSupportedException;
        }
    }
}
