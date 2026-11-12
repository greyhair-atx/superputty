using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace SuperPutty.Utils
{
    /// <summary>Retries on the UI message loop and abandons requests when their tab is no longer active.</summary>
    internal sealed class ChildFocusRetry : Component
    {
        private readonly Timer timer = new Timer();
        private Func<bool> canFocus;
        private Func<bool> tryFocus;
        private Action failed;
        private int remaining;
        private int generation;

        internal ChildFocusRetry()
        {
            timer.Tick += delegate { Attempt(); };
        }

        internal void Request(Func<bool> canFocus, Func<bool> tryFocus, Action failed, int attempts, int intervalMs)
        {
            Stop();
            this.canFocus = canFocus;
            this.tryFocus = tryFocus;
            this.failed = failed;
            remaining = Math.Max(1, attempts);
            timer.Interval = Math.Max(1, intervalMs);
            Attempt();
        }

        private void Attempt()
        {
            int request = generation;
            if (canFocus == null || !canFocus()) { Stop(); return; }
            bool focused = tryFocus();
            // Activating native windows can reenter the message loop and close or switch tabs.
            if (request != generation) return;
            if (focused) { Stop(); return; }
            if (--remaining == 0)
            {
                Action report = failed;
                Stop();
                report?.Invoke();
            }
            else timer.Start();
        }

        internal void Stop()
        {
            generation++;
            timer.Stop();
            canFocus = null;
            tryFocus = null;
            failed = null;
            remaining = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Stop(); timer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
