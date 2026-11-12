using System;
using System.Runtime.InteropServices;
using System.Threading;
using log4net;

namespace SuperPutty.Utils
{
    public class GlobalWindowEventArgs : EventArgs
    {
        public IntPtr hwnd {get; private set;}
        public uint eventType {get; private set;}

        public GlobalWindowEventArgs(IntPtr hwnd, uint eventType)
        {
            this.hwnd = hwnd;
            this.eventType = eventType;
        }
    }

    public class GlobalWindowEvents : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(GlobalWindowEvents));

        public event EventHandler<GlobalWindowEventArgs> SystemSwitch;
        private readonly int installingThreadId;
        private readonly Func<IntPtr, bool> unhook;
        private IntPtr m_hWinEventHook;
        private GCHandle callbackRoot;
        private bool disposing;
        private bool disposed;

        public GlobalWindowEvents()
            : this(RegisterHook, NativeMethods.UnhookWinEvent)
        {
        }

        internal GlobalWindowEvents(Func<NativeMethods.WinEventDelegate, IntPtr> register,
            Func<IntPtr, bool> unhook)
        {
            if (register == null) throw new ArgumentNullException("register");
            if (unhook == null) throw new ArgumentNullException("unhook");
            this.installingThreadId = Thread.CurrentThread.ManagedThreadId;
            this.unhook = unhook;

            // Windows retains a function pointer, not a managed reference. Root the
            // delegate (and its target) until the owning thread removes the hook.
            var callback = new NativeMethods.WinEventDelegate(WinEventProc);
            this.callbackRoot = GCHandle.Alloc(callback);
            try
            {
                this.m_hWinEventHook = register(callback);
                if (this.m_hWinEventHook == IntPtr.Zero)
                {
                    Log.Warn("Unable to register the system window-switch hook. Window-switch notifications are unavailable.");
                    this.callbackRoot.Free();
                }
            }
            catch
            {
                this.callbackRoot.Free();
                throw;
            }
        }

        private static IntPtr RegisterHook(NativeMethods.WinEventDelegate callback)
        {
            uint eventMin = (uint)NativeMethods.WinEvents.EVENT_SYSTEM_SWITCHSTART;
            uint eventMax = (uint)NativeMethods.WinEvents.EVENT_SYSTEM_SWITCHEND;
            return NativeMethods.SetWinEventHook(eventMin, eventMax, IntPtr.Zero, callback,
                0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (this.disposed || this.disposing) return;
            var handlers = this.SystemSwitch;
            if (handlers == null) return;

            var args = new GlobalWindowEventArgs(hwnd, eventType);
            foreach (EventHandler<GlobalWindowEventArgs> handler in handlers.GetInvocationList())
            {
                if (this.disposed || this.disposing) return;
                try { handler(this, args); }
                catch (Exception ex)
                {
                    // Exceptions must not escape a callback into unmanaged code.
                    Log.Error("Error handling a system window-switch notification", ex);
                }
            }
        }

        internal bool IsRegistered { get { return this.m_hWinEventHook != IntPtr.Zero; } }

        public void Dispose()
        {
            if (this.disposed) return;
            if (Thread.CurrentThread.ManagedThreadId != this.installingThreadId)
                throw new InvalidOperationException("Window event hooks must be removed on the thread that installed them.");

            this.disposing = true;
            try
            {
                if (this.m_hWinEventHook != IntPtr.Zero && !this.unhook(this.m_hWinEventHook))
                {
                    // Keep the delegate rooted if Windows might still call it.
                    // The owning thread can retry; Windows also removes its hooks
                    // when that thread exits.
                    Log.Warn("Unable to remove the system window-switch hook.");
                    return;
                }
                this.m_hWinEventHook = IntPtr.Zero;
                this.disposed = true;
                this.SystemSwitch = null;
                if (this.callbackRoot.IsAllocated) this.callbackRoot.Free();
            }
            finally { this.disposing = false; }
        }
    }
}
