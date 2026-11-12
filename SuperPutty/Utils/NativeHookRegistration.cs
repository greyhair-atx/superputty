using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using log4net;

namespace SuperPutty.Utils
{
    /// <summary>Owns a native hook and its managed callback until removal is confirmed.</summary>
    internal sealed class NativeHookRegistration : Component
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(NativeHookRegistration));
        private readonly int ownerThreadId;
        private readonly Func<IntPtr, bool> remove;
        private readonly string description;
        private GCHandle callbackRoot;
        private volatile bool callbacksEnabled;

        internal NativeHookRegistration(Delegate callback, Func<IntPtr> register,
            Func<IntPtr, bool> remove, string description)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (register == null) throw new ArgumentNullException(nameof(register));
            if (remove == null) throw new ArgumentNullException(nameof(remove));
            this.ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            this.remove = remove;
            this.description = description;
            this.callbackRoot = GCHandle.Alloc(callback);
            try
            {
                Handle = register();
                this.callbacksEnabled = Handle != IntPtr.Zero;
                if (!this.callbacksEnabled)
                {
                    this.callbackRoot.Free();
                    ReportFailure("Unable to register " + description + ".", null);
                }
            }
            catch
            {
                if (this.callbackRoot.IsAllocated) this.callbackRoot.Free();
                throw;
            }
        }

        internal IntPtr Handle { get; private set; }
        internal bool IsEnabled { get { return this.callbacksEnabled; } }
        internal bool IsRegistered { get { return Handle != IntPtr.Zero; } }

        /// <summary>May be retried by the owner after a failed removal.</summary>
        internal bool TryRemove()
        {
            // A native callback can still arrive after a failed unhook. It must
            // stop touching its owner even while we retain its delegate root.
            this.callbacksEnabled = false;
            if (Handle == IntPtr.Zero) return true;
            if (Thread.CurrentThread.ManagedThreadId != this.ownerThreadId)
            {
                ReportFailure("Unable to remove " + this.description + " from a different thread.", null);
                return false;
            }

            try
            {
                if (!this.remove(Handle))
                {
                    ReportFailure("Unable to remove " + this.description + "; callback ownership was retained.", null);
                    return false;
                }
            }
            catch (Exception ex)
            {
                ReportFailure("Error removing " + this.description + "; callback ownership was retained.", ex);
                return false;
            }

            Handle = IntPtr.Zero;
            if (this.callbackRoot.IsAllocated) this.callbackRoot.Free();
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) TryRemove();
            base.Dispose(disposing);
        }

        private static void ReportFailure(string message, Exception failure)
        {
            // Cleanup must continue even if an application-supplied log appender fails.
            try { Log.Warn(message, failure); }
            catch (Exception) { }
        }
    }
}
