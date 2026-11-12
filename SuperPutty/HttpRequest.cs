/*
 * Copyright (c) 2009 - 2015 Jim Radford http://www.jimradford.com
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions: 
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */
using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace SuperPutty
{
    internal static class UpdateRequestClient
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(UpdateRequestClient));
        internal const int MaximumResponseBytes = 1024 * 1024;
        internal const int RequestTimeoutMilliseconds = 10000;

        internal static bool TryGetSecureUri(string url, out Uri uri)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out uri) &&
                String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                !String.IsNullOrEmpty(uri.Host) &&
                String.IsNullOrEmpty(uri.UserInfo);
        }

        internal static async Task<string> DownloadJsonAsync(string url, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Uri uri;
            if (!TryGetSecureUri(url, out uri))
                throw new InvalidOperationException("Update requests require HTTPS without embedded credentials.");

            HttpWebRequest request = WebRequest.CreateHttp(uri);
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = RequestTimeoutMilliseconds;
            request.ReadWriteTimeout = RequestTimeoutMilliseconds;
            request.UserAgent = "SuperPuTTY/" + Assembly.GetExecutingAssembly().GetName().Version;
            request.Accept = "application/json";

            return await WithDeadlineAsync(async token =>
            {
                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                {
                    ValidateResponse(response.StatusCode, response.ContentType, response.ContentLength, response.ResponseUri);
                    using (Stream input = response.GetResponseStream())
                        return await ReadJsonAsync(input, token).ConfigureAwait(false);
                }
            }, request.Abort, cancellation).ConfigureAwait(false);
        }

        internal static void ValidateResponse(HttpStatusCode status, string contentType, long contentLength, Uri responseUri)
        {
            if ((int)status < 200 || (int)status >= 300)
                throw new InvalidDataException("Update request returned " + status + ".");
            Uri secureUri;
            if (responseUri == null || !responseUri.IsAbsoluteUri || !TryGetSecureUri(responseUri.AbsoluteUri, out secureUri))
                throw new InvalidDataException("Update response did not use credential-free HTTPS.");
            if (String.IsNullOrEmpty(contentType) || contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidDataException("Update response was not JSON.");
            if (contentLength > MaximumResponseBytes)
                throw new InvalidDataException("Update response exceeds the 1 MiB limit.");
        }

        internal static async Task<T> WithDeadlineAsync<T>(Func<CancellationToken, Task<T>> operation,
            Action abort, CancellationToken cancellation, int timeoutMilliseconds = RequestTimeoutMilliseconds)
        {
            cancellation.ThrowIfCancellationRequested();
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(timeoutMilliseconds);
                var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                int aborted = 0;
                Action abortOnce = () =>
                {
                    if (Interlocked.Exchange(ref aborted, 1) != 0) return;
                    try { abort(); }
                    catch (Exception ex) { Log.Warn("Could not abort a canceled update request", ex); }
                };
                using (deadline.Token.Register(() =>
                {
                    canceled.TrySetResult(true);
                    abortOnce();
                }))
                try
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    Task<T> pending = operation(deadline.Token);
                    if (await Task.WhenAny(pending, canceled.Task).ConfigureAwait(false) != pending)
                    {
                        // An abort normally releases network I/O. Stop waiting even if an operation
                        // ignores it, and observe any fault it produces after this request ends.
                        _ = pending.ContinueWith(completed => { var ignored = completed.Exception; },
                            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                        deadline.Token.ThrowIfCancellationRequested();
                    }
                    T result = await pending.ConfigureAwait(false);
                    deadline.Token.ThrowIfCancellationRequested();
                    return result;
                }
                catch (Exception ex) when (deadline.IsCancellationRequested)
                {
                    // Cooperative I/O cancellation can complete inline before this token's abort
                    // callback runs. Do not let registration disposal suppress the actual abort.
                    abortOnce();
                    cancellation.ThrowIfCancellationRequested();
                    throw new TimeoutException("Update check exceeded its overall time limit.", ex);
                }
            }
        }

        internal static async Task<string> ReadJsonAsync(Stream input, CancellationToken cancellation)
        {
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (output.Length + read > MaximumResponseBytes)
                        throw new InvalidDataException("Update response exceeds the 1 MiB limit.");
                    output.Write(buffer, 0, read);
                }
                cancellation.ThrowIfCancellationRequested();
                output.Position = 0;
                using (var reader = new StreamReader(output, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
        }
    }

}
