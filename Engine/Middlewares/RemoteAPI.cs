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
using System.Web;

namespace MatriX.API.Engine.Middlewares
{
    public class RemoteAPI
    {
        #region RemoteAPI
        private readonly RequestDelegate _next;

        IMemoryCache memory;

        IHttpClientFactory httpClientFactory;

        public static string serv(UserData userData, IMemoryCache mem)
        {
            Server[] working_servers = AppInit.settings.servers?.Where(i => 
                i.enable && 
                (i.group == userData.group || (i.groups != null && i.groups.Contains(userData.group))) && 
                i.status == 1 && 
                (i.workinghours == null || i.workinghours.Contains(DateTime.UtcNow.Hour))
            )?.ToArray();

            if (working_servers == null || working_servers.Length == 0)
            {
                // рабочие сервера у которых workinghours не совпадает с текущим Hour
                // Hour 16-18 / workinghours 19-24
                working_servers = AppInit.settings.servers?.Where(i => i.enable && i.status == 1 && i.workinghours != null && !i.workinghours.Contains(DateTime.UtcNow.Hour))?.ToArray();
                if (working_servers == null || working_servers.Length == 0)
                {
                    // резерв на случай перегрузки основных серверов
                    working_servers = AppInit.settings.servers?.Where(i => i.enable && i.status == 1 && i.reserve)?.ToArray();
                    if (working_servers == null || working_servers.Length == 0)
                        return "http://127.0.0.1";
                }    
            }

            if (!string.IsNullOrEmpty(userData.server))
            {
                if (working_servers.FirstOrDefault(i => i.host == userData.server) != null)
                    return userData.server;
            }

            string server = working_servers[Random.Shared.Next(0, working_servers.Length)].host;
            if (mem == null)
                return server;

            string mkey = $"RemoteAPI:serv:{userData.id}";
            if (mem.TryGetValue(mkey, out string _serv) && AppInit.settings.servers.FirstOrDefault(i => i.enable && i.status != 2 && i.host == _serv) != null)
            {
                mem.Set(mkey, _serv, DateTime.Now.AddHours(4));
                return _serv;
            }

            mem.Set(mkey, server, DateTime.Now.AddHours(4));

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
            if (userData.login == "service" || httpContext.Request.Path.Value.StartsWith("/torinfo") || httpContext.Request.Path.Value.StartsWith("/control") || httpContext.Request.Path.Value.StartsWith("/userdata"))
            {
                await _next(httpContext);
                return;
            }

            if (httpContext.Request.Path.Value.StartsWith("/search") && !AppInit.settings.onlyRemoteApi)
            {
                await _next(httpContext);
                return;
            }
            #endregion

            memory.Set($"RemoteAPI:{userData._ip}", userData, DateTime.Now.AddHours(5));

            string clearPath = HttpUtility.UrlDecode(httpContext.Request.Path.Value);
                   clearPath = Regex.Replace(clearPath, "[а-яА-Я]", "z");

            string clearUri = Regex.Replace(clearPath + httpContext.Request.QueryString.Value, @"[^\x00-\x7F]", "");

            string serip = serv(userData, memory);
            if (serip.Contains("127.0.0.1"))
            {
                if (AppInit.settings.onlyRemoteApi)
                {
                    await httpContext.Response.WriteAsync("no working servers", httpContext.RequestAborted).ConfigureAwait(false);
                    return;
                }

                await _next(httpContext);
                return;
            }

            if (Regex.IsMatch(httpContext.Request.Path.Value, "^/(stream|playlist|play/)"))
            {
                httpContext.Response.Redirect($"{serip}{clearUri}");
                return;
            }

            using (var client = httpClientFactory.CreateClient("base"))
            {
                var request = CreateProxyHttpRequest(httpContext, new Uri($"{serip}{clearUri}"), userData);
                var response = await client.SendAsync(request, httpContext.RequestAborted).ConfigureAwait(false);

                string result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

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

                            if (Regex.IsMatch(header.Key, @"[^\x00-\x7F]") || Regex.IsMatch(header.Value.ToString(), @"[^\x00-\x7F]"))
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

            requestMessage.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userData.login ?? userData.domainid}:{userData.passwd ?? "ts"}")));
            requestMessage.Headers.Add("X-group", userData.group.ToString());
            requestMessage.Headers.Add("X-shared", userData.shared.ToString());
            requestMessage.Headers.Add("X-Client-IP", userData._ip);
            requestMessage.Headers.Add("X-Versionts", userData.versionts ?? "latest");
            requestMessage.Headers.Add("X-maxSize", userData.maxSize.ToString());
            requestMessage.Headers.Add("X-maxiptoIsLockHostOrUser", userData.maxiptoIsLockHostOrUser.ToString());
            requestMessage.Headers.Add("X-allowedToChangeSettings", userData.allowedToChangeSettings.ToString());

            requestMessage.Headers.ConnectionClose = false;
            requestMessage.Headers.Host = uri.Authority;
            requestMessage.RequestUri = uri;
            requestMessage.Method = request == null ? new HttpMethod("GET") : new HttpMethod(request.Method);

            return requestMessage;
        }
        #endregion
    }
}
