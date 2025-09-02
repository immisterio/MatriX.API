using MatriX.API.Models;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace MatriX.API.Middlewares
{
    public class RemoteAPI
    {
        #region RemoteAPI
        private readonly RequestDelegate _next;

        IMemoryCache memory;

        IHttpClientFactory httpClientFactory;

        static readonly object lockObj = typeof(RemoteAPI);

        public RemoteAPI(RequestDelegate next, IMemoryCache memory, IHttpClientFactory httpClientFactory)
        {
            _next = next;
            this.memory = memory;
            this.httpClientFactory = httpClientFactory;
        }
        #endregion

        #region serv
        static string serv(UserData userData, string geo, IMemoryCache mem, bool isStream)
        {
            lock (lockObj)
            {
                // список активных серверов
                Server[] servers = AppInit.settings.servers?.Where(i =>
                    i.enable && (i.groups != null ? i.groups.Contains(userData.group) : i.group == userData.group)
                )?.Where(i =>
                    i.geo_hide == null || geo == null || !i.geo_hide.Contains(geo)
                )?.ToArray();


                if (!string.IsNullOrEmpty(userData.server) && servers != null)
                {
                    if (servers.FirstOrDefault(i => i.host == userData.server && (i.status == 1 || i.status == 3 && i.limit_hard != null && i.status_hard != 1)) != null)
                        return userData.server;
                }

                // сервера с проверкой workinghours
                Server[] working_servers = servers?.Where(i => i.status == 1 && (i.workinghours == null || i.workinghours.Contains(DateTime.UtcNow.Hour)))?.ToArray();

                if (working_servers == null || working_servers.Length == 0)
                {
                    // сервера без проверки workinghours
                    working_servers = servers?.Where(i => i.status == 1)?.ToArray();
                    if (working_servers == null || working_servers.Length == 0) 
                        return "http://127.0.0.1";
                }

                // принудительный сервер для всех кто использует auto
                string fserv = forcedServer(userData, servers?.Where(i => i.forced)?.ToArray(), mem);

                #region сервер к которому клиент уже привязан 
                string mkey = $"RemoteAPI:serv:{userData.id}";
                if (mem != null && mem.TryGetValue(mkey, out string _serv))
                {
                    if (isStream)
                        return _serv;

                    if (fserv != null)
                    {
                        mem.Set(mkey, fserv, DateTime.Now.AddHours(4));
                        return fserv;
                    }

                    if (servers.FirstOrDefault(i => i.host == _serv && i.status == 1) != null)
                    {
                        mem.Set(mkey, _serv, DateTime.Now.AddHours(4));
                        return _serv;
                    }
                }
                #endregion

                if (fserv != null)
                {
                    mem.Set(mkey, fserv, DateTime.Now.AddHours(4));
                    return fserv;
                }

                #region weight
                // 1. Получить массив рабочих серверов (working_servers).
                // 2. Для каждого сервера получить его вес (например, свойство weight).
                // 3. Сформировать список, где каждый сервер повторяется столько раз, каков его вес.
                // 4. Случайным образом выбрать сервер из этого списка.
                // 5. Вернуть host выбранного сервера.

                string server;
                if (working_servers.Length == 1)
                {
                    server = working_servers[0].host;
                }
                else
                {
                    var weightedList = new List<Server>();
                    foreach (var srv in working_servers)
                    {
                        for (int i = 0; i < srv.weight; i++)
                            weightedList.Add(srv);
                    }

                    server = weightedList[Random.Shared.Next(weightedList.Count)].host;
                }
                #endregion

                if (mem == null)
                    return server;

                mem.Set(mkey, server, DateTime.Now.AddHours(4));
                return server;
            }
        }
        #endregion

        #region servHard
        static string servHard(UserData userData, string geo, IMemoryCache memory, bool isStream)
        {
            lock (lockObj)
            {
                // рабочие сервера с лимитом нагрузки, но не перегружены в limit_hard
                Server[] working_servers = AppInit.settings.servers?.Where(i =>
                    i.enable && i.status == 3 && i.limit_hard != null && i.status_hard != 1 &&
                    (i.groups != null ? i.groups.Contains(userData.group) : i.group == userData.group)
                )?.Where(i =>
                    i.geo_hide == null || geo == null || !i.geo_hide.Contains(geo)
                )?.ToArray();

                if (working_servers == null || working_servers.Length == 0)
                    return null;

                string mkey = $"RemoteAPI:serv_hard:{userData.id}";
                if (memory != null && memory.TryGetValue(mkey, out string _serv))
                {
                    if (isStream)
                        return _serv;

                    if (working_servers.FirstOrDefault(i => i.host == _serv) != null)
                    {
                        memory.Set(mkey, _serv, DateTime.Now.AddHours(4));
                        return _serv;
                    }
                }

                string server = working_servers[Random.Shared.Next(working_servers.Length)].host;
                if (memory == null)
                    return server;

                memory.Set(mkey, server, DateTime.Now.AddHours(4));
                return server;
            }
        }
        #endregion

        async public Task InvokeAsync(HttpContext httpContext)
        {
            #region search / torinfo / control
            var userData = httpContext.Features.Get<UserData>();
            if (userData.login == "service" || httpContext.Request.Path.Value.StartsWith("/readbytes/") || httpContext.Request.Path.Value.StartsWith("/admin") || httpContext.Request.Path.Value.StartsWith("/control") || httpContext.Request.Path.Value.StartsWith("/userdata"))
            {
                if (!httpContext.Request.Path.Value.StartsWith("/userdata/slave"))
                {
                    await _next(httpContext);
                    return;
                }
            }

            if (httpContext.Request.Path.Value.StartsWith("/search"))
            {
                if (AppInit.settings.onlyRemoteApi == false || AppInit.settings.allowSearchOnlyRemoteApi)
                {
                    await _next(httpContext);
                    return;
                }

                await httpContext.Response.WriteAsync("search disabled", httpContext.RequestAborted).ConfigureAwait(false);
                return;
            }
            #endregion

            bool isStream = Regex.IsMatch(httpContext.Request.Path.Value, "^/(stream|playlist|play/|download/)");

            string serip = СurrentServer(userData, memory, isStream);
            if (string.IsNullOrEmpty(serip))
            {
                if (AppInit.settings.onlyRemoteApi)
                {
                    httpContext.Response.StatusCode = 403;
                    httpContext.Response.ContentType = "text/plain; charset=UTF-8";
                    await httpContext.Response.WriteAsync("no working servers", httpContext.RequestAborted).ConfigureAwait(false);
                    return;
                }

                await _next(httpContext);
                return;
            }

            memory.Set($"RemoteAPI:{userData._ip}", userData, DateTime.Now.AddDays(1));

            string clearPath = HttpUtility.UrlDecode(httpContext.Request.Path.Value.Replace("/userdata/slave", "/userdata"));
            clearPath = Regex.Replace(clearPath, "[а-яА-Я]", "z");

            string clearUri = Regex.Replace(clearPath + httpContext.Request.QueryString.Value, @"[^\x00-\x7F]", "");

            if (isStream)
            {
                if (httpContext.Request.Path.Value.StartsWith("/stream/") && Regex.IsMatch(httpContext.Request.QueryString.Value, "&(preload|stat|m3u)(&|$)", RegexOptions.IgnoreCase)) { }
                else
                {
                    #region maxReadBytesToHour
                    ulong maxReadBytes = AppInit.groupSettings(userData.group).maxReadBytesToHour;
                    if (maxReadBytes > 0 && AppInit.ReadBytesToHour.TryGetValue(userData.id, out ulong _readBytes) && _readBytes > maxReadBytes)
                    {
                        httpContext.Response.Redirect(AppInit.settings.maxReadBytes_urlVideoError);
                        return;
                    }
                    #endregion

                    if (AppInit.settings.remoteStream_pattern != null)
                    {
                        string domainid = userData.login ?? userData.domainid;
                        var g = Regex.Match(serip, AppInit.settings.remoteStream_pattern).Groups;

                        httpContext.Response.Redirect($"{g["sheme"]}://{domainid}.{g["server"]}" + clearUri);
                        return;
                    }
                    else
                    {
                        httpContext.Response.Redirect($"{serip}{clearUri}");
                        return;
                    }
                }
            }

            using (var client = httpClientFactory.CreateClient("base"))
            {
                var request = CreateProxyHttpRequest(httpContext, new Uri($"{serip}{clearUri}"), userData, serip);
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
        public static HttpRequestMessage CreateProxyHttpRequest(HttpContext context, Uri uri, UserData userData, string serip)
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
            requestMessage.Headers.Add("X-maxSize", Math.Max(AppInit.groupSettings(userData.group).maxSize, userData.maxSize).ToString());
            requestMessage.Headers.Add("X-maxiptoIsLockHostOrUser", Math.Max(AppInit.groupSettings(userData.group).maxiptoIsLockHostOrUser, userData.maxiptoIsLockHostOrUser).ToString());
            requestMessage.Headers.Add("X-maxIpToStream", Math.Max(AppInit.groupSettings(userData.group).maxIpToStream, userData.maxIpToStream).ToString());
            requestMessage.Headers.Add("X-allowedToChangeSettings", userData.allowedToChangeSettings.ToString());
            requestMessage.Headers.Add("X-shutdown", userData.shutdown.ToString());

            if (AppInit.settings.servers != null && serip != null)
                requestMessage.Headers.Add("X-SlaveName", HttpUtility.UrlEncode(AppInit.settings.servers.FirstOrDefault(i => i.host != null && i.host.StartsWith(serip))?.name ?? "unknown"));

            requestMessage.Headers.ConnectionClose = false;
            requestMessage.Headers.Host = uri.Authority;
            requestMessage.RequestUri = uri;
            requestMessage.Method = request == null ? new HttpMethod("GET") : new HttpMethod(request.Method);

            return requestMessage;
        }
        #endregion

        #region СurrentServer
        public static string СurrentServer(UserData userData, IMemoryCache memory, bool isStream)
        {
            string geo = GeoIP2.Country(userData._ip);
            string serip = serv(userData, geo, memory, isStream);
            if (serip.Contains("127.0.0.1"))
            {
                serip = servHard(userData, geo, memory, isStream);
                if (string.IsNullOrEmpty(serip))
                {
                    serip = AppInit.settings.reserve_server;
                    if (string.IsNullOrEmpty(serip))
                        return null;
                }
            }

            return serip;
        }
        #endregion


        #region forcedServer
        static string forcedServer(UserData userData, Server[] forcedServers, IMemoryCache mem)
        {
            if (forcedServers == null || forcedServers.Length == 0 || mem == null)
                return null;

            if (forcedServers.Length == 1)
                return forcedServers.First().host;

            string mkey = $"RemoteAPI:forcedServer:{userData.id}";
            if (mem.TryGetValue(mkey, out string _serv))
            {
                if (forcedServers.FirstOrDefault(i => i.host == _serv && i.status == 1) != null)
                {
                    mem.Set(mkey, _serv, DateTime.Now.AddHours(4));
                    return _serv;
                }
            }

            string server = forcedServers[Random.Shared.Next(forcedServers.Length)].host;
            mem.Set(mkey, server, DateTime.Now.AddHours(4));

            return server;
        }
        #endregion
    }
}
