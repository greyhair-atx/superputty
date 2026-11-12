using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace SuperPutty.Utils
{
    internal sealed class SingleInstanceEndpoint
    {
        internal SecurityIdentifier User { get; private set; }
        internal string PipeName { get; private set; }
        internal string MutexName { get; private set; }

        internal static SingleInstanceEndpoint Current
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                using (var process = Process.GetCurrentProcess())
                    return ForUser(identity.User.Value, process.SessionId);
            }
        }

        internal static SingleInstanceEndpoint ForUser(string sid, int session)
        {
            if (session < 0) throw new ArgumentOutOfRangeException(nameof(session));
            var user = new SecurityIdentifier(sid);
            string suffix;
            using (var hash = SHA256.Create())
                suffix = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(user.Value + ":" + session)))
                    .Replace("-", String.Empty);
            return new SingleInstanceEndpoint
            {
                User = user,
                PipeName = "SuperPuTTY.CE.SingleInstance.v1." + suffix,
                MutexName = "Local\\SuperPuTTY.CE.SingleInstance." + suffix
            };
        }

        internal PipeSecurity CreatePipeSecurity()
        {
            var security = new PipeSecurity();
            security.SetOwner(User);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(User, PipeAccessRights.FullControl, AccessControlType.Allow));
            return security;
        }

        internal MutexSecurity CreateMutexSecurity()
        {
            var security = new MutexSecurity();
            security.SetOwner(User);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                MutexRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new MutexAccessRule(User, MutexRights.FullControl, AccessControlType.Allow));
            return security;
        }
    }

    /// <summary>Acquired and released on the application's main thread.</summary>
    internal sealed class SingleInstanceMutex : IDisposable
    {
        private Mutex mutex;
        internal bool IsPrimary { get; private set; }

        internal SingleInstanceMutex(SingleInstanceEndpoint endpoint)
        {
            bool created;
            mutex = new Mutex(false, endpoint.MutexName, out created, endpoint.CreateMutexSecurity());
            try
            {
                if (!endpoint.User.Equals(mutex.GetAccessControl().GetOwner(typeof(SecurityIdentifier))))
                    throw new UnauthorizedAccessException("The single-instance mutex owner could not be verified.");
                try { IsPrimary = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { IsPrimary = true; }
            }
            catch { mutex.Dispose(); mutex = null; throw; }
        }

        internal static bool LegacyInstanceExists()
        {
            try
            {
                using (Mutex.OpenExisting("Local\\SuperPutty", MutexRights.Synchronize | MutexRights.ReadPermissions)) return true;
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }

        public void Dispose()
        {
            if (mutex == null) return;
            try { if (IsPrimary) mutex.ReleaseMutex(); }
            finally { mutex.Dispose(); mutex = null; IsPrimary = false; }
        }
    }

    internal static class SingleInstancePipeClient
    {
        internal static Task<SingleInstanceReply> SendAsync(SingleInstanceEndpoint endpoint, string[] args,
            CancellationToken cancellation, TimeSpan? timeout = null)
        {
            return SendAsync(endpoint.PipeName, endpoint.User, args, cancellation, timeout);
        }

        internal static async Task<SingleInstanceReply> SendAsync(string pipeName, SecurityIdentifier expectedOwner,
            string[] args, CancellationToken cancellation, TimeSpan? timeout = null)
        {
            TimeSpan limit = timeout ?? TimeSpan.FromSeconds(3);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var pipe = new NamedPipeClientStream(".", pipeName, PipeAccessRights.ReadWrite | PipeAccessRights.ReadPermissions,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None))
            {
                deadline.CancelAfter(limit);
                using (deadline.Token.Register(() => Close(pipe)))
                try
                {
                    await pipe.ConnectAsync((int)Math.Min(Int32.MaxValue, Math.Max(1, limit.TotalMilliseconds)), deadline.Token).ConfigureAwait(false);
                    return await ExchangeAuthenticatedAsync(pipe, args, () =>
                    {
                        if (!expectedOwner.Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier))))
                            throw new UnauthorizedAccessException("The running instance's pipe owner could not be verified.");
                    }, deadline.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (deadline.IsCancellationRequested)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new TimeoutException("The running instance did not respond within the time limit.", ex);
                }
            }
        }

        internal static async Task<SingleInstanceReply> ExchangeAuthenticatedAsync(Stream stream, string[] args,
            Action verifyPeer, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (verifyPeer == null) throw new ArgumentNullException(nameof(verifyPeer));
            // Never send command-line passwords to an unverified, pre-created pipe.
            verifyPeer();
            await SingleInstancePipeProtocol.WriteRequestAsync(stream, args, cancellation).ConfigureAwait(false);
            return await SingleInstancePipeProtocol.ReadReplyAsync(stream, cancellation).ConfigureAwait(false);
        }

        private static void Close(IDisposable pipe)
        {
            try { pipe.Dispose(); }
            catch (IOException) { }
        }
    }

    internal sealed class SingleInstancePipeServer : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SingleInstancePipeServer));
        private readonly object gate = new object();
        private readonly string pipeName;
        private readonly Func<string[], SingleInstanceReply> enqueue;
        private readonly TimeSpan requestTimeout;
        private readonly CancellationTokenSource stopping = new CancellationTokenSource();
        private NamedPipeServerStream active;
        private bool stopped;
        internal Task Completion { get; private set; }

        internal SingleInstancePipeServer(string pipeName, Func<string[], SingleInstanceReply> enqueue, TimeSpan? requestTimeout = null)
        {
            this.pipeName = pipeName;
            this.enqueue = enqueue ?? throw new ArgumentNullException(nameof(enqueue));
            this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(2);
            try
            {
                active = CreatePipe();
                Completion = Task.Run(() => ServeAsync(active));
                _ = Completion.ContinueWith(task => Log.Warn("The single-instance listener stopped unexpectedly."),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            catch { active?.Dispose(); stopping.Dispose(); throw; }
        }

        private NamedPipeServerStream CreatePipe()
        {
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 4096, 4096, SingleInstanceEndpoint.Current.CreatePipeSecurity());
        }

        private async Task ServeAsync(NamedPipeServerStream first)
        {
            var pipe = first;
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
                    using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
                    {
                        deadline.CancelAfter(requestTimeout);
                        using (deadline.Token.Register(() => Close(pipe)))
                        {
                            SingleInstanceReply reply;
                            try
                            {
                                string[] args = await SingleInstancePipeProtocol.ReadRequestAsync(pipe, deadline.Token).ConfigureAwait(false);
                                deadline.Token.ThrowIfCancellationRequested();
                                reply = enqueue(args);
                            }
                            catch (NotSupportedException) { reply = SingleInstanceReply.UnsupportedVersion; }
                            catch (Exception ex) when (ex is InvalidDataException || ex is EndOfStreamException)
                            {
                                // Malformed payload exceptions must not echo argument data.
                                reply = SingleInstanceReply.InvalidRequest;
                            }
                            await SingleInstancePipeProtocol.WriteReplyAsync(pipe, reply, deadline.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is OperationCanceledException)
                {
                    // An abandoned or stalled client must not retain the listener.
                }
                finally
                {
                    lock (gate) if (Object.ReferenceEquals(active, pipe)) active = null;
                    Close(pipe);
                }
                lock (gate)
                {
                    if (stopped) return;
                    pipe = CreatePipe();
                    active = pipe;
                }
            }
        }

        private static void Close(IDisposable pipe)
        {
            try { pipe?.Dispose(); }
            catch (IOException) { }
        }

        public void Dispose()
        {
            NamedPipeServerStream pipe;
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
                pipe = active;
                active = null;
            }
            stopping.Cancel();
            Close(pipe);
            _ = Completion.ContinueWith(task => stopping.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
