using System;
using System.Collections.Generic;
using System.Linq;

namespace SuperPutty.Utils
{
    /// <summary>Bounded UI admission. An accepted request belongs to the application,
    /// independent of the transport connection or its request timeout.</summary>
    internal sealed class SingleInstanceCommandQueue : IDisposable
    {
        private readonly object gate = new object();
        private readonly SortedDictionary<long, string[]> pending = new SortedDictionary<long, string[]>();
        private readonly HashSet<long> scheduled = new HashSet<long>();
        private readonly Action<Action> post;
        private readonly Action<string[]> dispatch;
        private readonly Func<bool> ready;
        private readonly Func<bool> canDispatch;
        private readonly Action<Exception> reportFailure;
        private readonly int maximumPending;
        private long nextId;
        private bool stopped;
        private bool dispatching;

        internal SingleInstanceCommandQueue(Action<Action> post, Action<string[]> dispatch, Func<bool> ready,
            int maximumPending = 16, Action<Exception> reportFailure = null, Func<bool> canDispatch = null)
        {
            this.post = post ?? throw new ArgumentNullException(nameof(post));
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.ready = ready ?? throw new ArgumentNullException(nameof(ready));
            this.canDispatch = canDispatch ?? (() => true);
            if (maximumPending < 1) throw new ArgumentOutOfRangeException(nameof(maximumPending));
            this.maximumPending = maximumPending;
            this.reportFailure = reportFailure;
        }

        internal int PendingCount { get { lock (gate) return pending.Count; } }

        internal SingleInstanceReply TryEnqueue(string[] args)
        {
            if (args == null) return SingleInstanceReply.InvalidRequest;
            long id;
            lock (gate)
            {
                if (stopped || !ready()) return SingleInstanceReply.NotReady;
                if (pending.Count >= maximumPending) return SingleInstanceReply.Busy;
                id = ++nextId;
                pending.Add(id, (string[])args.Clone());
            }
            if (!Post(id)) return SingleInstanceReply.NotReady;
            lock (gate) return stopped ? SingleInstanceReply.NotReady : SingleInstanceReply.Accepted;
        }

        private bool Post(long id)
        {
            lock (gate)
            {
                if (stopped || !pending.ContainsKey(id)) return false;
                if (!scheduled.Add(id)) return true;
            }
            try { post(() => Execute(id)); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                lock (gate) { pending.Remove(id); scheduled.Remove(id); }
                ReportFailure(ex);
                return false;
            }
            return true;
        }

        private void Execute(long id)
        {
            string[] args;
            lock (gate)
            {
                scheduled.Remove(id);
                if (!pending.TryGetValue(id, out args)) return;
                // Modal dialogs pump callbacks. Keep admitted commands queued
                // until the active command or layout transition has returned.
                if (stopped || dispatching || !ready() || !canDispatch() || id != pending.Keys.First()) return;
                pending.Remove(id);
                dispatching = true;
            }
            try { dispatch(args); }
            catch (Exception ex) { ReportFailure(ex); }
            finally
            {
                lock (gate) dispatching = false;
                ResumePending();
            }
        }

        internal void ResumePending()
        {
            long[] ids;
            lock (gate)
            {
                if (stopped || dispatching || !ready() || !canDispatch()) return;
                ids = new long[pending.Count];
                pending.Keys.CopyTo(ids, 0);
            }
            foreach (long id in ids) Post(id);
        }

        private void ReportFailure(Exception error)
        {
            try { reportFailure?.Invoke(error); }
            catch (Exception) { } // Diagnostics must not break admission or UI dispatch.
        }

        internal void Stop()
        {
            lock (gate) { stopped = true; pending.Clear(); scheduled.Clear(); }
        }

        public void Dispose() { Stop(); }
    }
}
