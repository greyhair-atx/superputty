using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SuperPutty.Utils
{
    // All participants lock the canonical destination, never a staging filename.
    internal static class SharedFile
    {
        internal sealed class ChangedException : IOException
        {
            internal ChangedException(string path) : base("The file changed in another instance. Reload it before saving (restart SuperPuTTY for preferences): " + path) { }
        }

        internal const int WaitMilliseconds = 2000;
        [ThreadStatic] private static Stopwatch operation;

        // These APIs are synchronous. Nested lock/read/publish operations share a
        // single contention deadline on their owning thread instead of restarting it.
        private sealed class Operation : IDisposable
        {
            private readonly bool owner;
            internal Operation()
            {
                owner = operation == null;
                if (owner) operation = Stopwatch.StartNew();
            }
            public void Dispose() { if (owner) operation = null; }
        }

        private static int Remaining { get { return Math.Max(0, WaitMilliseconds - (int)operation.ElapsedMilliseconds); } }

        internal static IDisposable Lock(string path)
        {
            var scope = new Operation();
            Mutex mutex = null;
            try
            {
                string identity = Path.GetFullPath(path).ToUpperInvariant();
                mutex = new Mutex(false, "Local\\SuperPuTTY.File." + Hash(Encoding.UTF8.GetBytes(identity)));
                bool acquired;
                try { acquired = mutex.WaitOne(Remaining); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("The file is busy in another SuperPuTTY instance. Try again.");
                return new Lease(mutex, scope);
            }
            catch { mutex?.Dispose(); scope.Dispose(); throw; }
        }

        private sealed class Lease : IDisposable
        {
            private Mutex mutex;
            private readonly Operation scope;
            internal Lease(Mutex mutex, Operation scope) { this.mutex = mutex; this.scope = scope; }
            public void Dispose()
            {
                if (mutex == null) return;
                try { mutex.ReleaseMutex(); }
                finally { mutex.Dispose(); mutex = null; scope.Dispose(); }
            }
        }

        internal static T Retry<T>(Func<T> action, CancellationToken cancellation = default(CancellationToken))
        {
            using (new Operation())
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                try { return action(); }
                catch (IOException ex) when (((ex.HResult & 65535) == 32 || (ex.HResult & 65535) == 33) && Remaining > 0)
                {
                    if (cancellation.WaitHandle.WaitOne(Math.Min(25, Remaining))) cancellation.ThrowIfCancellationRequested();
                }
            }
        }

        internal static FileStream OpenRead(string path, CancellationToken cancellation = default(CancellationToken))
        {
            return Retry(() => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), cancellation);
        }

        internal sealed class FileVersion
        {
            internal bool Missing;
            internal string Revision;
            internal FileIdentity Identity;
        }

        // Capture metadata before the size check so recovery can preserve an
        // oversized invalid primary without reading or hashing all of its bytes.
        internal static byte[] Capture(string path, int maximumBytes, out FileVersion version,
            CancellationToken cancellation = default(CancellationToken))
        {
            version = new FileVersion { Missing = true };
            using (new Operation())
            using (var input = OpenRead(path, cancellation))
            {
                version = new FileVersion { Identity = Identity(input) };
                byte[] bytes = ReadBounded(input, maximumBytes, cancellation);
                version.Revision = Hash(bytes);
                return bytes;
            }
        }

        internal static byte[] Read(string path, CancellationToken cancellation = default(CancellationToken))
        {
            return Read(path, Int32.MaxValue, cancellation);
        }

        internal static byte[] Read(string path, int maximumBytes, CancellationToken cancellation = default(CancellationToken))
        {
            using (new Operation())
            using (var input = OpenRead(path, cancellation)) return ReadBounded(input, maximumBytes, cancellation);
        }

        internal static byte[] ReadBounded(FileStream input, int maximumBytes, CancellationToken cancellation = default(CancellationToken))
        {
            if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            long length = input.Length;
            if (length > maximumBytes) throw new InvalidDataException("The file exceeds its supported size limit.");
            // One exact-size allocation. The reader denies writes to this file;
            // replacements may proceed while this handle retains the old snapshot.
            var bytes = new byte[(int)length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                cancellation.ThrowIfCancellationRequested();
                int read = input.Read(bytes, offset, Math.Min(81920, bytes.Length - offset));
                if (read == 0) throw new IOException("The file changed while it was being read.");
                offset += read;
            }
            cancellation.ThrowIfCancellationRequested();
            if (input.ReadByte() != -1) throw new IOException("The file changed while it was being read.");
            return bytes;
        }

        internal static string Hash(byte[] bytes)
        {
            if (bytes == null) return null;
            using (var hash = SHA256.Create()) return Format(hash.ComputeHash(bytes));
        }

        private static string Hash(Stream stream)
        {
            stream.Position = 0;
            using (var hash = SHA256.Create()) return Format(hash.ComputeHash(stream));
        }

        private static string Format(byte[] hash) { return BitConverter.ToString(hash).Replace("-", ""); }

        internal static string WriteStaged(string path, Action<Stream> serialize, int maximumBytes = Int32.MaxValue)
        {
            if (serialize == null) throw new ArgumentNullException(nameof(serialize));
            using (var hash = SHA256.Create())
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var hashing = new CryptoStream(output, hash, CryptoStreamMode.Write, true))
                {
                    serialize(hashing);
                    hashing.FlushFinalBlock();
                    if (output.Length > maximumBytes) throw new InvalidDataException("The file exceeds its supported size limit.");
                    output.Flush(true);
                    return Format(hash.Hash);
                }
            }
        }

        internal static string Revision(string path)
        {
            using (new Operation())
            using (var input = OpenOptional(path)) return input == null ? null : Hash(input);
        }

        internal static FileStream OpenOptional(string path)
        {
            try { return OpenRead(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        internal static void Publish(string temporary, string path, string backup, string expected)
        {
            PublishVersion(temporary, path, backup, new FileVersion { Missing = expected == null, Revision = expected });
        }

        internal static void PublishVersion(string temporary, string path, string backup, FileVersion expected)
        {
            using (new Operation())
            using (var verified = OpenOptional(path))
            {
                if ((verified == null) != expected.Missing) throw new ChangedException(path);
                FileIdentity identity = default(FileIdentity);
                if (verified != null)
                {
                    identity = Identity(verified);
                    if (expected.Revision != null)
                    {
                        if (!String.Equals(Hash(verified), expected.Revision, StringComparison.Ordinal)) throw new ChangedException(path);
                    }
                    else if (!identity.Equals(expected.Identity)) throw new ChangedException(path);
                }

                PublishVerified(temporary, path, backup, verified == null, identity);
            }
        }

        // The caller holds an open handle that denies writes to the verified file.
        // Detect replacement of its directory entry without rehashing its bytes.
        internal static void PublishVerified(string temporary, string path, string backup, bool missing, FileIdentity identity)
        {
            Retry(() =>
            {
                using (var current = OpenOptional(path))
                {
                    if ((current == null) != missing || (current != null && !Identity(current).Equals(identity)))
                        throw new ChangedException(path);
                    if (missing) File.Move(temporary, path);
                    else File.Replace(temporary, path, backup);
                }
                return true;
            });
        }

        internal static bool IsSameDestination(string first, string second)
        {
            first = Path.GetFullPath(first);
            second = Path.GetFullPath(second);
            if (String.Equals(first, second, StringComparison.OrdinalIgnoreCase)) return true;
            using (var a = OpenOptional(first))
            using (var b = OpenOptional(second))
            {
                if (a != null && b != null) return Identity(a).SameFile(Identity(b));
            }
            if (!String.Equals(Path.GetFileName(first), Path.GetFileName(second), StringComparison.OrdinalIgnoreCase)) return false;
            // A junction can alias a destination even before the file exists.
            const uint share = 7, openExisting = 3, backupSemantics = 0x02000000;
            using (var a = CreateFile(Path.GetDirectoryName(first), 0, share, IntPtr.Zero, openExisting, backupSemantics, IntPtr.Zero))
            using (var b = CreateFile(Path.GetDirectoryName(second), 0, share, IntPtr.Zero, openExisting, backupSemantics, IntPtr.Zero))
            {
                FileIdentity left, right;
                return !a.IsInvalid && !b.IsInvalid && GetFileInformationByHandle(a, out left) &&
                    GetFileInformationByHandle(b, out right) && left.SameFile(right);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileIdentity : IEquatable<FileIdentity>
        {
            private uint attributes, creationLow, creationHigh, accessLow, accessHigh;
            private uint writeLow, writeHigh, volume, sizeHigh, sizeLow, links, indexHigh, indexLow;
            public bool Equals(FileIdentity other)
            {
                return SameFile(other) &&
                    sizeHigh == other.sizeHigh && sizeLow == other.sizeLow && writeLow == other.writeLow && writeHigh == other.writeHigh;
            }
            internal bool SameFile(FileIdentity other)
            {
                return volume == other.volume && indexHigh == other.indexHigh && indexLow == other.indexLow;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileIdentity information);

        internal static FileIdentity Identity(FileStream input)
        {
            FileIdentity identity;
            if (!GetFileInformationByHandle(input.SafeFileHandle, out identity))
                throw new IOException("Unable to identify the open file.", new Win32Exception(Marshal.GetLastWin32Error()));
            return identity;
        }
    }
}
