using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace SuperPutty.Data
{
    internal interface ISessionStorage
    {
        Task<List<SessionData>> LoadAsync(string location, CancellationToken cancellation);
        void Save(List<SessionData> sessions, string fileName);
    }

    /// <summary>Loads a complete catalog before its caller publishes any changes to the UI.</summary>
    internal sealed class SessionStorage : ISessionStorage
    {
        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(typeof(SessionStorage));
        private readonly Func<string, CancellationToken, Task<List<SessionData>>> readLocal;
        private readonly Func<Uri, CancellationToken, Task<List<SessionData>>> readRemote;
        private readonly TimeSpan operationTimeout;
        internal const int MaximumCollections = 128;
        internal const int MaximumSessions = 100000;

        internal SessionStorage(
            Func<string, CancellationToken, Task<List<SessionData>>> readLocal = null,
            Func<Uri, CancellationToken, Task<List<SessionData>>> readRemote = null,
            TimeSpan? operationTimeout = null)
        {
            this.readLocal = readLocal ?? ReadLocalAsync;
            this.readRemote = readRemote ?? ReadRemoteAsync;
            this.operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(30);
        }

        public async Task<List<SessionData>> LoadAsync(string location, CancellationToken cancellation)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(operationTimeout);
                try
                {
                    var budget = new LoadBudget();
                    var result = await LoadCollectionAsync(location, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        0, budget, deadline.Token).ConfigureAwait(false);
                    deadline.Token.ThrowIfCancellationRequested();
                    return result;
                }
                catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !cancellation.IsCancellationRequested)
                {
                    throw new TimeoutException("Loading sessions exceeded the overall time limit.", ex);
                }
            }
        }

        private sealed class LoadBudget
        {
            internal int Collections;
            internal int Sessions;
        }

        private async Task<List<SessionData>> LoadCollectionAsync(string location, HashSet<string> active,
            int depth, LoadBudget budget, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (String.IsNullOrWhiteSpace(location)) return new List<SessionData>();
            if (depth > SessionData.MaximumCollectionDepth || ++budget.Collections > MaximumCollections)
                throw new InvalidDataException("Session collection nesting or count exceeds the supported limit.");

            Uri uri;
            bool web = Uri.TryCreate(location, UriKind.Absolute, out uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            if (web && (uri.Scheme != Uri.UriSchemeHttps || String.IsNullOrEmpty(uri.Host) || !String.IsNullOrEmpty(uri.UserInfo)))
                throw new InvalidDataException("Remote collections require HTTPS without embedded credentials.");

            string localPath = !web && location.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(location).LocalPath : location;
            string key = web ? uri.AbsoluteUri : Path.GetFullPath(localPath);
            if (!active.Add(key)) throw new InvalidDataException("Circular session collection reference detected.");
            try
            {
                var sessions = await AwaitReadAsync(web ? readRemote(uri, cancellation) : readLocal(localPath, cancellation),
                    cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (sessions == null) throw new InvalidDataException("The session collection is empty or invalid.");
                if (sessions.Exists(session => session == null))
                    throw new InvalidDataException("A session collection contains an invalid entry.");
                if (web) SessionData.ResolveRemoteSessionLocations(sessions, uri);
                var expanded = new List<SessionData>();
                foreach (var session in sessions)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (session == null) throw new InvalidDataException("A session collection contains an invalid entry.");
                    if (String.IsNullOrEmpty(session.CollectionLocation))
                    {
                        if (++budget.Sessions > MaximumSessions)
                            throw new InvalidDataException("Session collection expansion exceeds the supported session count.");
                        expanded.Add(session);
                        continue;
                    }
                    var children = await LoadCollectionAsync(session.CollectionLocation, active, depth + 1,
                        budget, cancellation).ConfigureAwait(false);
                    string prefix = (session.CollectionID ?? String.Empty).Trim('/');
                    foreach (var child in children)
                    {
                        if (prefix.Length > 0)
                            child.SessionId = prefix + "/" + (child.SessionId ?? child.SessionName ?? String.Empty).TrimStart('/');
                        expanded.Add(child);
                    }
                }
                return expanded;
            }
            finally { active.Remove(key); }
        }

        private static async Task<List<SessionData>> AwaitReadAsync(Task<List<SessionData>> read, CancellationToken cancellation)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellation.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(read, canceled.Task).ConfigureAwait(false) != read)
                {
                    // A synchronous local filesystem read may finish after cancellation.
                    // Observe a late failure, but never publish its eventual result.
                    _ = read.ContinueWith(completed => { var ignored = completed.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    cancellation.ThrowIfCancellationRequested();
                }
                cancellation.ThrowIfCancellationRequested();
                return await read.ConfigureAwait(false);
            }
        }

        private static Task<List<SessionData>> ReadLocalAsync(string path, CancellationToken cancellation)
        {
            return Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                using (var reader = XmlReader.Create(path, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
                }))
                {
                    var sessions = (List<SessionData>)new XmlSerializer(typeof(List<SessionData>)).Deserialize(reader);
                    cancellation.ThrowIfCancellationRequested();
                    return sessions;
                }
            }, cancellation);
        }

        private static async Task<List<SessionData>> ReadRemoteAsync(Uri uri, CancellationToken cancellation)
        {
            var request = WebRequest.CreateHttp(uri);
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = request.ReadWriteTimeout = 10000;
            request.UserAgent = "SuperPuTTY-Sessions";
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                using (deadline.Token.Register(request.Abort))
                try
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                    {
                        if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                            throw new InvalidDataException("Remote collection returned " + response.StatusCode + ".");
                        if (response.ContentLength > SessionData.MaximumRemoteCollectionBytes)
                            throw new InvalidDataException("Remote collection exceeds the 1 MiB limit.");
                        using (var input = response.GetResponseStream())
                        using (var output = new MemoryStream())
                        {
                            var buffer = new byte[8192];
                            int read;
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) > 0)
                            {
                                deadline.Token.ThrowIfCancellationRequested();
                                if (output.Length + read > SessionData.MaximumRemoteCollectionBytes)
                                    throw new InvalidDataException("Remote collection exceeds the 1 MiB limit.");
                                output.Write(buffer, 0, read);
                            }
                            deadline.Token.ThrowIfCancellationRequested();
                            output.Position = 0;
                            using (var reader = XmlReader.Create(output, new XmlReaderSettings
                            {
                                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
                            }))
                                return (List<SessionData>)new XmlSerializer(typeof(List<SessionData>)).Deserialize(reader);
                        }
                    }
                }
                catch (Exception ex) when (deadline.IsCancellationRequested)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new TimeoutException("Remote collection download exceeded its overall time limit.", ex);
                }
            }
        }

        public void Save(List<SessionData> sessions, string fileName)
        {
            if (sessions == null)
            {
                throw new ArgumentNullException("sessions");
            }
            if (String.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("A session file name must be provided.", "fileName");
            }

            Log.InfoFormat("Saving {0} sessions to {1}", sessions.Count, fileName);

            string fullPath = Path.GetFullPath(fileName);
            string directory = Path.GetDirectoryName(fullPath);
            string tempFile = Path.Combine(
                directory,
                Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            List<SessionData> sessionsToSave = new List<SessionData>(sessions);
            sessionsToSave.Sort();
            XmlSerializer serializer = new XmlSerializer(sessionsToSave.GetType());

            try
            {
                using (FileStream stream = new FileStream(
                    tempFile,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                using (TextWriter writer = new StreamWriter(stream))
                {
                    serializer.Serialize(writer, sessionsToSave);
                    writer.Flush();
                    stream.Flush(true);
                }

                SessionData.BackUpFiles(fullPath, 20);
                if (File.Exists(fullPath))
                {
                    File.Replace(tempFile, fullPath, null);
                }
                else
                {
                    File.Move(tempFile, fullPath);
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
    }
}
