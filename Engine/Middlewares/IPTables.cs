using MatriX.API.Models;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.IO;
using System;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;
using System.Net;

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

        #region IsLockHostOrUser
        bool IsLockHostOrUser(UserData user, out HashSet<string> ips)
        {
            string memKeyLocIP = $"memKeyLocIP:{user.id}:{DateTime.Now.Hour}";
            string clientIP = user._ip;

            #region whiteip
            var clientIP2 = IPAddress.Parse(clientIP);
            foreach (var whiteip in AppInit.whiteip)
            {
                if (whiteip.Contains(clientIP2))
                {
                    ips = new HashSet<string>();
                    return false;
                }
            }
            #endregion

            if (memoryCache.TryGetValue(memKeyLocIP, out ips))
            {
                ips.Add(clientIP);
                memoryCache.Set(memKeyLocIP, ips, DateTime.Now.AddHours(1));

                int maxiptoIsLockHostOrUser = AppInit.settings.maxiptoIsLockHostOrUser;
                if (user.maxiptoIsLockHostOrUser > 0)
                    maxiptoIsLockHostOrUser = user.maxiptoIsLockHostOrUser;

                if (ips.Count > maxiptoIsLockHostOrUser)
                    return true;
            }
            else
            {
                ips = new HashSet<string>() { clientIP };
                memoryCache.Set(memKeyLocIP, ips, DateTime.Now.AddHours(1));
            }

            return false;
        }
        #endregion

        public Task InvokeAsync(HttpContext httpContext)
        {
            if (httpContext.Request.Path.Value.StartsWith("/favicon.ico"))
                return Task.CompletedTask;

            var userData = httpContext.Features.Get<UserData>();
            if (userData.login == "service" || httpContext.Request.Path.Value.StartsWith("/torinfo") || httpContext.Request.Path.Value.StartsWith("/control"))
                return _next(httpContext);

            if (userData.expires != default && DateTime.Now > userData.expires)
            {
                httpContext.Response.StatusCode = 403;
                httpContext.Response.ContentType = "text/plain; charset=UTF-8";
                return httpContext.Response.WriteAsync("Доступ запрещен, причина: дата");
            }

            if (IsLockHostOrUser(userData, out HashSet<string> ips))
            {
                httpContext.Response.StatusCode = 403;
                httpContext.Response.ContentType = "text/plain; charset=UTF-8";
                return httpContext.Response.WriteAsync($"Превышено допустимое количество ip. Разбан через {60 - DateTime.Now.Minute} мин.\n\n" + string.Join(", ", ips));
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
                return httpContext.Response.WriteAsync($"IP {clientIP} отсутствует в списке разрешенных");
            }

            return _next(httpContext);
        }
    }
}
