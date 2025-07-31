using MatriX.API.Models;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.IO;
using System;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace MatriX.API.Engine.Middlewares
{
    public class IPTables
    {
        #region IPTables
        private readonly RequestDelegate _next;

        IMemoryCache memoryCache;

        public IPTables(RequestDelegate next, IMemoryCache memoryCache)
        {
            this.memoryCache = memoryCache;
            _next = next;
        }
        #endregion

        #region IsWhiteIP
        bool IsWhiteIP(string clientIP)
        {
            var clientIP2 = IPAddress.Parse(clientIP);
            foreach (var whiteip in AppInit.whiteip)
            {
                if (whiteip.Contains(clientIP2))
                    return true;
            }

            return false;
        }
        #endregion

        #region IsLockHostOrUser
        bool IsLockHostOrUser(UserData user, out HashSet<string> ips)
        {
            string memKeyLocIP = $"memKeyLocIP:{user.id}:{DateTime.Now.Hour}";

            if (memoryCache.TryGetValue(memKeyLocIP, out ips))
            {
                ips.Add(user._ip);
                memoryCache.Set(memKeyLocIP, ips, DateTime.Now.AddHours(1));

                int maxiptoIsLockHostOrUser = AppInit.settings.maxiptoIsLockHostOrUser;
                if (user.maxiptoIsLockHostOrUser > 0)
                    maxiptoIsLockHostOrUser = user.maxiptoIsLockHostOrUser;

                if (ips.Count > maxiptoIsLockHostOrUser)
                    return true;
            }
            else
            {
                ips = new HashSet<string>() { user._ip };
                memoryCache.Set(memKeyLocIP, ips, DateTime.Now.AddHours(1));
            }

            return false;
        }
        #endregion

        #region IsLockStream
        bool IsLockStream(string requestPath, UserData user, out HashSet<string> ips)
        {
            string memKeyLocIP = $"memKeyLocIP:stream:{user.id}:{DateTime.Now.Hour}";

            if (memoryCache.TryGetValue(memKeyLocIP, out ips))
            {
                if (requestPath.StartsWith("/stream"))
                {
                    ips.Add(user._ip);
                    memoryCache.Set(memKeyLocIP, ips, DateTime.Now.AddHours(1));
                }

                int maxip = AppInit.settings.maxIpToStream;
                if (user.maxIpToStream > 0)
                    maxip = user.maxIpToStream;

                if (ips.Count > maxip)
                    return true;
            }
            else
            {
                ips = new HashSet<string>() { user._ip };

                if (requestPath.StartsWith("/stream"))
                    memoryCache.Set(memKeyLocIP, ips, DateTime.Now.AddHours(1));
            }

            return false;
        }
        #endregion

        public Task InvokeAsync(HttpContext httpContext)
        {
            var userData = httpContext.Features.Get<UserData>();
            if (userData.login == "service" || userData.login == "default" || httpContext.Request.Path.Value.StartsWith("/torinfo") || httpContext.Request.Path.Value.StartsWith("/control") || httpContext.Request.Path.Value.StartsWith("/userdata"))
                return _next(httpContext);

            if (httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgent))
            {
                if (Regex.IsMatch(userAgent.ToString(), "(Googlebot|Yandex(bot|Images|Direct)|bingbot|Baiduspider|DuckDuckBot|Slurp)", RegexOptions.IgnoreCase))
                {
                    httpContext.Response.StatusCode = 403;
                    httpContext.Response.ContentType = "text/plain; charset=UTF-8";
                    return httpContext.Response.WriteAsync("Доступ запрещен для поисковых ботов", httpContext.RequestAborted);
                }
            }

            if (userData.expires != default && DateTime.Now > userData.expires)
            {
                httpContext.Response.StatusCode = 403;
                httpContext.Response.ContentType = "text/plain; charset=UTF-8";
                return httpContext.Response.WriteAsync("Доступ запрещен, причина: дата", httpContext.RequestAborted);
            }

            if (AppInit.settings.group > 0 && AppInit.settings.group > userData.group)
            {
                httpContext.Response.StatusCode = 403;
                httpContext.Response.ContentType = "text/plain; charset=UTF-8";
                return httpContext.Response.WriteAsync($"Доступ запрещен, причина: group {AppInit.settings.group} > {userData.group}", httpContext.RequestAborted);
            }

            if (IsWhiteIP(userData._ip) == false)
            {
                HashSet<string> ips;
                if (IsLockHostOrUser(userData, out ips) || IsLockStream(httpContext.Request.Path.Value, userData, out ips))
                {
                    httpContext.Response.StatusCode = 403;
                    httpContext.Response.ContentType = "text/plain; charset=UTF-8";
                    return httpContext.Response.WriteAsync($"Превышено допустимое количество ip. Разбан через {60 - DateTime.Now.Minute} мин.\n\n" + string.Join(", ", ips), httpContext.RequestAborted);
                }
            }

            if (userData.whiteip != null && userData.whiteip.Count > 0)
            {
                string clientIP = userData._ip;

                var clientIPAddress = IPAddress.Parse(clientIP);
                foreach (string whiteip in userData.whiteip)
                {
                    if (whiteip.Contains("/"))
                    {
                        if (int.TryParse(whiteip.Split("/")[1], out int prefixLength))
                            if (new IPNetwork(IPAddress.Parse(whiteip.Split("/")[0]), prefixLength).Contains(clientIPAddress))
                                return _next(httpContext);
                    }
                    else
                    {
                        if (new IPNetwork(IPAddress.Parse(whiteip), 0).Contains(clientIPAddress))
                            return _next(httpContext);
                    }
                }

                httpContext.Response.StatusCode = 403;
                httpContext.Response.ContentType = "text/plain; charset=UTF-8";
                return httpContext.Response.WriteAsync($"IP {clientIP} отсутствует в списке разрешенных", httpContext.RequestAborted);
            }

            if (AppInit.settings.onlyRemoteApi == false && httpContext.Request.Path.Value.StartsWith("/stream/"))
            {
                if (TorAPI.db.TryGetValue(userData.id, out TorInfo tinfo))
                {
                    var filtered = tinfo.filteredActiveStreams;
                    string tlink = Regex.Match(httpContext.Request.QueryString.Value, @"link=([0-9a-z]+)", RegexOptions.IgnoreCase).Groups[1].Value;
                    string tindex = Regex.Match(httpContext.Request.QueryString.Value, @"index=([0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value;

                    if (!filtered.ContainsKey($"{tlink}_{tindex}"))
                    {
                        if (filtered.Count >= AppInit.settings.rateLimiter.limitStream)
                        {
                            httpContext.Response.Redirect(AppInit.settings.rateLimiter.urlVideoError);
                            return Task.CompletedTask;
                        }
                    }
                }
            }

            return _next(httpContext);
        }
    }
}
