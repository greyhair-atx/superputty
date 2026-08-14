/*
 * Copyright (c) 2009 - 2015 Jim Radford http://www.jimradford.com
 * Licensed under the MIT License. See License.txt in the repository root.
 */
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;

namespace SuperPutty
{
    public class httpRequest
    {
        private static readonly HttpClient Client = CreateClient();

        public async void MakeRequest(string url, Action<bool, string> callback)
        {
            try
            {
                string content = await Client.GetStringAsync(url).ConfigureAwait(false);
                callback(true, content);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                callback(false, ex.Message);
            }
        }

        public static string GetString(string url)
        {
            return Client.GetStringAsync(url).GetAwaiter().GetResult();
        }

        private static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
                "SuperPuTTY",
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.6.0"));
            return client;
        }
    }
}
