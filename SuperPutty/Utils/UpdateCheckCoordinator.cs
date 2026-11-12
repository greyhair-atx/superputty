using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace SuperPutty.Utils
{
    /// <summary>Owns update checks and publishes only the latest result on the caller's context.</summary>
    internal sealed class UpdateCheckCoordinator : Component
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(UpdateCheckCoordinator));
        private readonly object sync = new object();
        private CancellationTokenSource active;
        private bool disposed;

        internal bool IsRunning { get { lock (sync) return active != null; } }

        internal async Task RunAsync(Func<CancellationToken, Task<string>> load, Action<bool, string> publish)
        {
            if (load == null) throw new ArgumentNullException(nameof(load));
            if (publish == null) throw new ArgumentNullException(nameof(publish));
            CancellationTokenSource previous, current;
            lock (sync)
            {
                if (disposed) return;
                previous = active;
                current = new CancellationTokenSource();
                active = current;
            }
            Cancel(previous);
            try
            {
                string content;
                bool success;
                try
                {
                    current.Token.ThrowIfCancellationRequested();
                    content = await load(current.Token);
                    success = true;
                }
                catch (OperationCanceledException) when (current.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    content = ex.Message;
                    success = false;
                }
                lock (sync)
                {
                    if (!disposed && ReferenceEquals(active, current) && !current.IsCancellationRequested)
                        publish(success, content);
                }
            }
            finally
            {
                lock (sync)
                    if (ReferenceEquals(active, current)) active = null;
                current.Dispose();
            }
        }

        internal void OnClosing(bool canceled)
        {
            if (canceled) return;
            CancellationTokenSource pending;
            lock (sync) { pending = active; active = null; }
            Cancel(pending);
        }

        private static void Cancel(CancellationTokenSource source)
        {
            try { source?.Cancel(); }
            catch (ObjectDisposedException) { } // The completed operation may have released it already.
            catch (AggregateException ex) { Log.Warn("An update cancellation callback failed", ex); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (sync) disposed = true;
                OnClosing(false);
            }
            base.Dispose(disposing);
        }
    }
}
