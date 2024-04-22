using MatriX.API.Models;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Memory;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MatriX.API.Engine.Middlewares
{
    public class RemoteAPI
    {
        #region RemoteAPI
        private readonly RequestDelegate _next;

        IMemoryCache memory;

        IHttpClientFactory httpClientFactory;

        public static string serv(UserData userData)
        {
            if (AppInit.settings.servers == null || AppInit.settings.servers.Count == 0)
                return "http://127.0.0.1";

            string server = AppInit.settings.servers[0].host;

            if (!string.IsNullOrEmpty(userData.server))
                server = userData.server;

            if (AppInit.settings.servers.FirstOrDefault(i => i.host == server) == null)
                server = AppInit.settings.servers[0].host;

            return server;
        }

        public RemoteAPI(RequestDelegate next, IMemoryCache memory, IHttpClientFactory httpClientFactory)
        {
            _next = next;
            this.memory = memory;
            this.httpClientFactory = httpClientFactory;
        }
        #endregion

        async public Task InvokeAsync(HttpContext httpContext)
        {
            #region search / torinfo / control
            var userData = httpContext.Features.Get<UserData>();
            if (userData.login == "service" || httpContext.Request.Path.Value.StartsWith("/search") || httpContext.Request.Path.Value.StartsWith("/torinfo") || httpContext.Request.Path.Value.StartsWith("/control"))
            {
                await _next(httpContext);
                return;
            }
            #endregion

            string serip = serv(userData);
            if (serip.Contains("127.0.0.1"))
            {
                await _next(httpContext);
                return;
            }

            memory.Set($"RemoteAPI:{userData._ip}", userData, DateTime.Now.AddHours(5));

            if (Regex.IsMatch(httpContext.Request.Path.Value, "^/(stream|playlist|play/)"))
            {
                httpContext.Response.Redirect($"{serip}{httpContext.Request.Path.Value + httpContext.Request.QueryString.Value}");
                return;
            }

            using (var client = httpClientFactory.CreateClient("base"))
            {
                var request = CreateProxyHttpRequest(httpContext, new Uri($"{serip}{httpContext.Request.Path.Value + httpContext.Request.QueryString.Value}"), userData);
                var response = await client.SendAsync(request, httpContext.RequestAborted);

                string result = await response.Content.ReadAsStringAsync();

                httpContext.Response.StatusCode = (int)response.StatusCode;
                httpContext.Response.ContentLength = response.Content.Headers.ContentLength;

                #region UpdateHeaders
                void UpdateHeaders(HttpHeaders headers)
                {
                    foreach (var header in headers)
                    {
                        try
                        {
                            if (header.Key.ToLower() is "www-authenticate" or "transfer-encoding" or "etag" or "connection" or "content-disposition")
                                continue;

                            httpContext.Response.Headers.TryAdd(header.Key, header.Value.ToArray());
                        }
                        catch { }
                    }
                }
                #endregion

                UpdateHeaders(response.Headers);
                UpdateHeaders(response.Content.Headers);

                await httpContext.Response.WriteAsync(result, httpContext.RequestAborted).ConfigureAwait(false);
            }
        }


        #region CreateProxyHttpRequest
        public static HttpRequestMessage CreateProxyHttpRequest(HttpContext context, Uri uri, UserData userData)
        {
            var request = context?.Request;

            var requestMessage = new HttpRequestMessage();

            if (request != null)
            {
                var requestMethod = request.Method;
                if (HttpMethods.IsPost(requestMethod))
                {
                    var streamContent = new StreamContent(request.Body);
                    requestMessage.Content = streamContent;
                }

                foreach (var header in request.Headers)
                {
                    try
                    {
                        if (header.Key.ToLower() is "authorization")
                            continue;

                        if (Regex.IsMatch(header.Key, @"[^\x00-\x7F]") || Regex.IsMatch(header.Value.ToString(), @"[^\x00-\x7F]"))
                            continue;

                        if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && requestMessage.Content != null)
                            requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                    }
                    catch { }
                }
            }
            
            requestMessage.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userData.login}:{userData.passwd}")));
            requestMessage.Headers.Add("X-Client-IP", userData._ip);
            requestMessage.Headers.Add("X-Versionts", userData.versionts ?? "latest");

            requestMessage.Headers.ConnectionClose = false;
            requestMessage.Headers.Host = uri.Authority;
            requestMessage.RequestUri = uri;
            requestMessage.Method = request == null ? new HttpMethod("GET") : new HttpMethod(request.Method);

            return requestMessage;
        }
        #endregion
    }
}
