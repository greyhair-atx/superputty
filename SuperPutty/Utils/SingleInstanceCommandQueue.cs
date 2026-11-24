using System;
using System.Collections.Generic;

namespace SuperPutty.Utils
{
    /// <summary>Bounded UI admission. An accepted request belongs to the application,
    /// independent of the transport connection or its request timeout.</summary>
    internal sealed class SingleInstanceCommandQueue : IDisposable
    {
        private readonly object gate = new object();
        private readonly Dictionary<long, string[]> pending = new Dictionary<long, string[]>();
        private readonly HashSet<long> scheduled = new HashSet<long>();
        private readonly Action<Action> post;
        private readonly Action<string[]> dispatch;
        private readonly Func<bool> ready;
        private readonly Action<Exception> reportFailure;
        private readonly int maximumPending;
        private long nextId;
        private bool stopped;

        internal SingleInstanceCommandQueue(Action<Action> post, Action<string[]> dispatch, Func<bool> ready,
            int maximumPending = 16, Action<Exception> reportFailure = null)
        {
            this.post = post ?? throw new ArgumentNullException(nameof(post));
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.ready = ready ?? throw new ArgumentNullException(nameof(ready));
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
                // A modal exit confirmation can pump posted UI callbacks. Keep
                // accepted work so canceling the exit can resume it.
                if (!ready() && !stopped) return;
                pending.Remove(id);
                if (stopped) return;
            }
            try { dispatch(args); }
            catch (Exception ex) { ReportFailure(ex); }
        }

        internal void ResumePending()
        {
            long[] ids;
            lock (gate)
            {
                if (stopped || !ready()) return;
                ids = new long[pending.Count];
                pending.Keys.CopyTo(ids, 0);
            }
            Array.Sort(ids);
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
