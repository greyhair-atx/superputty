using System;
using System.Threading;
using System.Windows.Forms;
using SuperPutty.Data;
using log4net;

namespace SuperPutty.Utils
{
    public static class SingleInstanceHelper
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SingleInstanceHelper));
        private static Registration registration;

        internal static bool TryLaunchInExistingInstance(string[] args, Action<string[]> launch = null)
        {
            try
            {
                (launch ?? LaunchInExistingInstance)(args);
                return true;
            }
            catch (Exception ex)
            {
                // Exception messages can originate in untrusted command arguments.
                Log.Warn("Unable to forward command to the running instance (" + ex.GetType().Name + ").");
                return false;
            }
        }

        public static void LaunchInExistingInstance(string[] args)
        {
            var reply = SingleInstancePipeClient.SendAsync(SingleInstanceEndpoint.Current, args, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (reply != SingleInstanceReply.Accepted)
                throw new InvalidOperationException("The running instance could not accept the command: " + reply + ".");
        }

        internal static IDisposable StartReceiving(Form form, SingleInstanceEndpoint endpoint, Action<string[]> dispatch = null)
        {
            if (registration != null) throw new InvalidOperationException("A single-instance listener is already registered.");
            registration = new Registration(form, endpoint, dispatch ?? DispatchToApplication);
            return registration;
        }

        internal static void PauseReceiving() { registration?.Pause(); }
        internal static void ResumeReceiving() { registration?.Resume(); }
        internal static void StopReceiving() { registration?.Dispose(); }

        private static void DispatchToApplication(string[] args)
        {
            DispatchCommand(args,
                name =>
                {
                    var layout = SuperPuTTY.FindLayout(name);
                    if (layout == null) throw new ArgumentException("The requested layout was not found.");
                    SuperPuTTY.LoadLayout(layout);
                }, SuperPuTTY.OpenSession);
        }

        // The main UI thread owns this registration; the pipe worker only reads
        // the volatile readiness flag and submits to the bounded command queue.
        private sealed class Registration : IDisposable
        {
            private readonly Form form;
            private readonly SingleInstanceCommandQueue queue;
            private readonly SingleInstancePipeServer server;
            private volatile bool receiving;
            private bool paused;
            private bool shown;
            private bool disposed;

            internal Registration(Form form, SingleInstanceEndpoint endpoint, Action<string[]> dispatch)
            {
                this.form = form ?? throw new ArgumentNullException(nameof(form));
                if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
                queue = new SingleInstanceCommandQueue(action => form.BeginInvoke(action), dispatch,
                    () => receiving && !form.IsDisposed && !form.Disposing && form.IsHandleCreated,
                    reportFailure: error => Log.Warn("A forwarded command could not be executed (" + error.GetType().Name + ")."));
                try { server = new SingleInstancePipeServer(endpoint.PipeName, queue.TryEnqueue); }
                catch { queue.Dispose(); throw; }
                form.Shown += FormShown;
                form.Disposed += FormDisposed;
            }

            private void FormShown(object sender, EventArgs args)
            {
                if (disposed || form.IsDisposed || form.Disposing || !form.IsHandleCreated) return;
                // Load has already queued the initial layout. Admit commands after
                // that work, rather than resolving sessions against startup state.
                form.BeginInvoke((Action)(() =>
                {
                    if (disposed) return;
                    shown = true;
                    receiving = !paused;
                    queue.ResumePending();
                }));
            }

            internal void Pause() { paused = true; receiving = false; }
            internal void Resume()
            {
                paused = false;
                if (disposed || !shown) return;
                receiving = true;
                queue.ResumePending();
            }
            private void FormDisposed(object sender, EventArgs args) { Dispose(); }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                receiving = false;
                queue.Stop();
                server.Dispose();
                form.Shown -= FormShown;
                form.Disposed -= FormDisposed;
                if (Object.ReferenceEquals(registration, this)) registration = null;
            }
        }

        internal static void DispatchCommand(string[] args, Action<string> loadLayout, Action<SessionDataStartInfo> openSession)
        {
            var cmd = new CommandLineOptions(args);
            if (!cmd.IsValid) return;
            if (cmd.Layout != null)
                loadLayout(cmd.Layout);
            else
            {
                var session = cmd.ToSessionStartInfo();
                if (session != null) openSession(session);
            }
        }
    }
}
