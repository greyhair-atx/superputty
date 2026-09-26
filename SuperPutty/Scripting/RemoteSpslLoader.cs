using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SuperPuTTY.Scripting
{
    /// <summary>Downloads explicitly trusted SPSL scripts using bounded HTTPS requests.</summary>
    internal static class RemoteSpslLoader
    {
        internal const int MaximumScriptBytes = 1024 * 1024;
        internal const int RequestTimeoutMilliseconds = 10000;

        internal static bool TryGetSecureUri(string location, out Uri uri)
        {
            return Uri.TryCreate(location, UriKind.Absolute, out uri) &&
                String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                !String.IsNullOrEmpty(uri.Host) &&
                String.IsNullOrEmpty(uri.UserInfo);
        }

        internal static async Task<string> DownloadAsync(Uri uri, CancellationToken cancellation)
        {
            Uri secureUri;
            if (uri == null || !TryGetSecureUri(uri.AbsoluteUri, out secureUri))
                throw new InvalidOperationException("Remote SPSL scripts must use HTTPS.");

            HttpWebRequest request = WebRequest.CreateHttp(secureUri);
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = RequestTimeoutMilliseconds;
            request.ReadWriteTimeout = RequestTimeoutMilliseconds;
            request.UserAgent = "SuperPuTTY-SPSL";

            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(RequestTimeoutMilliseconds);
                using (deadline.Token.Register(request.Abort))
                try
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                    {
                        if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                            throw new InvalidOperationException("Remote SPSL request returned " + response.StatusCode + ".");
                        if (!String.Equals(response.ResponseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Remote SPSL response did not use HTTPS.");
                        if (response.ContentLength > MaximumScriptBytes)
                            throw new InvalidOperationException("Remote SPSL script exceeds the 1 MiB limit.");

                        using (Stream input = response.GetResponseStream())
                            return await ReadScriptAsync(input, deadline.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (deadline.IsCancellationRequested)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new TimeoutException("Remote script download exceeded its overall time limit.", ex);
                }
            }
        }

        internal static async Task<string> ReadScriptAsync(Stream input, CancellationToken cancellation)
        {
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (output.Length + read > MaximumScriptBytes)
                        throw new InvalidOperationException("Remote SPSL script exceeds the 1 MiB limit.");
                    output.Write(buffer, 0, read);
                }
                cancellation.ThrowIfCancellationRequested();
                output.Position = 0;
                using (StreamReader reader = new StreamReader(output, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
        }
    }
}
