using System;
using System.IO;
using System.Net;
using System.Text;

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

        internal static string Download(Uri uri)
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

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                    throw new InvalidOperationException("Remote SPSL request returned " + response.StatusCode + ".");
                if (!String.Equals(response.ResponseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Remote SPSL response did not use HTTPS.");
                if (response.ContentLength > MaximumScriptBytes)
                    throw new InvalidOperationException("Remote SPSL script exceeds the 1 MiB limit.");

                using (Stream input = response.GetResponseStream())
                using (MemoryStream output = new MemoryStream())
                {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > MaximumScriptBytes)
                            throw new InvalidOperationException("Remote SPSL script exceeds the 1 MiB limit.");
                        output.Write(buffer, 0, read);
                    }

                    output.Position = 0;
                    using (StreamReader reader = new StreamReader(output, Encoding.UTF8, true))
                        return reader.ReadToEnd();
                }
            }
        }
    }
}
