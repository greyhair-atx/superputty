using System;
using System.IO;
using System.Security;

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
                    "The SuperPuTTY settings folder is not writable: " + fallbackFolder,
                    configuredFolderError);
            }

            Exception fallbackError;
            if (!TryEnsureWritable(fallbackFolder, out fallbackError))
            {
                throw new IOException(
                    "Neither the configured SuperPuTTY settings folder nor the Local AppData fallback is writable.",
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

        internal static void CopyExistingSettings(string sourceFolder, string destinationFolder)
        {
            if (String.IsNullOrWhiteSpace(sourceFolder) ||
                String.IsNullOrWhiteSpace(destinationFolder) ||
                PathsEqual(sourceFolder, destinationFolder) ||
                !Directory.Exists(sourceFolder))
            {
                return;
            }

            Directory.CreateDirectory(destinationFolder);
            CopyIfMissing(sourceFolder, destinationFolder, "Sessions.XML");
            CopyIfMissing(sourceFolder, destinationFolder, "AutoRestoreLayout.XML");

            string sourceLayouts = Path.Combine(sourceFolder, "layouts");
            if (!Directory.Exists(sourceLayouts))
            {
                return;
            }

            string destinationLayouts = Path.Combine(destinationFolder, "layouts");
            Directory.CreateDirectory(destinationLayouts);
            foreach (string sourceFile in Directory.GetFiles(sourceLayouts, "*.xml"))
            {
                string destinationFile = Path.Combine(destinationLayouts, Path.GetFileName(sourceFile));
                if (!File.Exists(destinationFile))
                {
                    File.Copy(sourceFile, destinationFile, false);
                }
            }
        }

        private static void CopyIfMissing(string sourceFolder, string destinationFolder, string fileName)
        {
            string source = Path.Combine(sourceFolder, fileName);
            string destination = Path.Combine(destinationFolder, fileName);
            if (File.Exists(source) && !File.Exists(destination))
            {
                File.Copy(source, destination, false);
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
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
