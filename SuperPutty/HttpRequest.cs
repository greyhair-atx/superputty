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

namespace SuperPutty
{
    internal sealed class UpdateRequestClient
    {
        internal const int MaximumResponseBytes = 1024 * 1024;
        private const int RequestTimeoutMilliseconds = 10000;

        public void MakeRequest(string url, Action<bool, string> callback)
        {
            if (callback == null)
                throw new ArgumentNullException("callback");

            ThreadPool.QueueUserWorkItem(delegate
            {
                bool success;
                string content;
                try
                {
                    content = DownloadJson(url);
                    success = true;
                }
                catch (Exception ex)
                {
                    content = ex.Message;
                    success = false;
                }
                callback(success, content);
            });
        }

        internal static bool TryGetSecureUri(string url, out Uri uri)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out uri) &&
                String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                !String.IsNullOrEmpty(uri.Host) &&
                String.IsNullOrEmpty(uri.UserInfo);
        }

        internal static string DownloadJson(string url)
        {
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

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                    throw new InvalidDataException("Update request returned " + response.StatusCode + ".");
                if (String.IsNullOrEmpty(response.ContentType) ||
                    response.ContentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidDataException("Update response was not JSON.");
                if (response.ContentLength > MaximumResponseBytes)
                    throw new InvalidDataException("Update response exceeds the 1 MiB limit.");

                using (Stream input = response.GetResponseStream())
                using (MemoryStream output = new MemoryStream())
                {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > MaximumResponseBytes)
                            throw new InvalidDataException("Update response exceeds the 1 MiB limit.");
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
