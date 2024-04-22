using MatriX.API.Models;
using Microsoft.AspNetCore.Http;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace MatriX.API.Engine.Middlewares
{
    public class Accs
    {
        #region Accs
        private readonly RequestDelegate _next;

        IMemoryCache memory;

        public Accs(RequestDelegate next, IMemoryCache memory)
        {
            _next = next;
            this.memory = memory;
        }
        #endregion

        public Task Invoke(HttpContext httpContext)
        {
            #region Служебный запрос
            string clientIp = httpContext.Connection.RemoteIpAddress.ToString();

            if (clientIp == "127.0.0.1" || httpContext.Request.Path.Value.StartsWith("/xrealip") || httpContext.Request.Path.Value.StartsWith("/headers"))
            {
                httpContext.Features.Set(new UserData()
                {
                    login = "service"
                });

                return _next(httpContext);
            }
            #endregion

            #region Авторизация по домену
            string domainid = Regex.Match(httpContext.Request.Host.Value, "^([^\\.]+)\\.").Groups[1].Value;

            if (!string.IsNullOrWhiteSpace(domainid))
            {
                if (AppInit.usersDb.FirstOrDefault(i => i.domainid == domainid) is UserData _domainUser)
                {
                    httpContext.Features.Set(_domainUser);
                    return _next(httpContext);
                }
            }
            #endregion

            #region Обработка stream потока и msx
            if (httpContext.Request.Method == "GET" && Regex.IsMatch(httpContext.Request.Path.Value, "^/(stream|playlist|play/|msx/)"))
            {
                if (TorAPI.db.LastOrDefault(i => i.Value.clientIps.Contains(clientIp)).Value is TorInfo info)
                {
                    httpContext.Features.Set(info.user);
                    return _next(httpContext);
                }
                else if (memory.TryGetValue($"RemoteAPI:{clientIp}", out UserData _u))
                {
                    httpContext.Features.Set(_u);
                    return _next(httpContext);
                }
                else
                {
                    httpContext.Response.StatusCode = 404;
                    return Task.CompletedTask;
                }
            }
            #endregion

            #region Access-Control-Request-Headers
            if (httpContext.Request.Method == "OPTIONS" && httpContext.Request.Headers.TryGetValue("Access-Control-Request-Headers", out var AccessControl) && AccessControl == "authorization")
            {
                httpContext.Response.StatusCode = 204;
                return Task.CompletedTask;
            }
            #endregion

            if (httpContext.Request.Headers.TryGetValue("Authorization", out var Authorization))
            {
                try
                {
                    byte[] data = Convert.FromBase64String(Authorization.ToString().Replace("Basic ", ""));
                    string[] decodedString = Encoding.UTF8.GetString(data).Split(":");

                    string login = Regex.Replace(decodedString[0], "[^a-zA-Z0-9\\.\\-_\\@]+", "");
                    string passwd = decodedString[1];

                    if (!string.IsNullOrEmpty(login) && !string.IsNullOrEmpty(passwd))
                    {
                        if (AppInit.settings.AuthorizationServerAPI == clientIp)
                        {
                            string xcip = httpContext.Request.Headers["X-Client-IP"].ToString();
                            string versionts = httpContext.Request.Headers["X-Versionts"].ToString();
                            httpContext.Features.Set(new UserData() { login = login, passwd = passwd, _ip = xcip, versionts = versionts, expires = DateTime.Now.AddDays(1) });
                            return _next(httpContext);
                        }
                        else if (AppInit.settings.AuthorizationRequired)
                        {
                            if (AppInit.usersDb.FirstOrDefault(i => i.login == login) is UserData _u && _u.passwd == passwd)
                            {
                                _u._ip = clientIp;
                                httpContext.Features.Set(_u);
                                return _next(httpContext);
                            }
                        }
                        else
                        {
                            httpContext.Features.Set(new UserData() { login = login, passwd = passwd, _ip = clientIp, expires = DateTime.Now.AddDays(1) });
                            return _next(httpContext);
                        }
                    }
                }
                catch { }
            }

            if (httpContext.Request.Path.Value.StartsWith("/echo"))
                return httpContext.Response.WriteAsync("MatriX.API");

            httpContext.Response.StatusCode = 401;
            httpContext.Response.Headers.Append("Www-Authenticate", "Basic realm=Authorization Required");
            return Task.CompletedTask;
        }
    }
}
