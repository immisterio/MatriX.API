using MatriX.API.Models;
using Microsoft.AspNetCore.Http;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using System.IO;

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
            if (httpContext.Request.Path.Value.StartsWith("/favicon.ico"))
            {
                httpContext.Response.BodyWriter.WriteAsync(File.ReadAllBytes("favicon.ico")).ConfigureAwait(false);
                return Task.CompletedTask;
            }

            string clientIp = httpContext.Connection.RemoteIpAddress.ToString();

            if (clientIp == "127.0.0.1" || 
                httpContext.Request.Path.Value.StartsWith("/top") || 
                httpContext.Request.Path.Value.StartsWith("/xrealip") || 
                httpContext.Request.Path.Value.StartsWith("/headers"))
            {
                httpContext.Features.Set(new UserData()
                {
                    login = "service"
                });

                return _next(httpContext);
            }
            #endregion

            #region Авторизация по домену
            if (!string.IsNullOrEmpty(AppInit.settings.domainid_pattern) && AppInit.settings.AuthorizationServerAPI != clientIp)
            {
                string domainid = Regex.Match(httpContext.Request.Host.Value, AppInit.settings.domainid_pattern).Groups[1].Value;

                if (!string.IsNullOrWhiteSpace(domainid))
                {
                    if (AppInit.usersDb.FirstOrDefault(i => i.domainid == domainid) is UserData _domainUser)
                    {
                        if (_domainUser.shared)
                        {
                            var ushared = _domainUser.Clone();
                            ushared._ip = clientIp;
                            ushared.id = $"{domainid}/{clientIp.Replace(":", "_")}";

                            httpContext.Features.Set(ushared);
                            return _next(httpContext);
                        }

                        _domainUser._ip = clientIp;
                        _domainUser.id = domainid;
                        httpContext.Features.Set(_domainUser);
                        return _next(httpContext);
                    }
                    else if (AppInit.settings.AuthorizationRequired == false)
                    {
                        httpContext.Features.Set(new UserData() { id = domainid, login = domainid, passwd = "ts", _ip = clientIp, expires = DateTime.Now.AddDays(1) });
                        return _next(httpContext);
                    }

                    if (AppInit.settings.UserNotFoundToError)
                    {
                        if (httpContext.Request.Path.Value.StartsWith("/echo"))
                            return httpContext.Response.WriteAsync("MatriX.API");

                        httpContext.Response.StatusCode = 403;
                        httpContext.Response.ContentType = "text/plain; charset=utf-8";
                        return httpContext.Response.WriteAsync(AppInit.settings.UserNotFoundToMessage);
                    }
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
                    _u.id = _u.login ?? _u.domainid;
                    _u._ip = clientIp;
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
                            long maxSize = 0;
                            if (httpContext.Request.Headers.ContainsKey("X-maxSize") && long.TryParse(httpContext.Request.Headers["X-maxSize"].ToString(), out long _maxSize))
                                maxSize = _maxSize;

                            byte maxiptoIsLockHostOrUser = 0;
                            if (httpContext.Request.Headers.ContainsKey("X-maxiptoIsLockHostOrUser") && byte.TryParse(httpContext.Request.Headers["X-maxiptoIsLockHostOrUser"].ToString(), out byte _maxiptoIsLockHostOrUser))
                                maxiptoIsLockHostOrUser = _maxiptoIsLockHostOrUser;

                            bool allowedToChangeSettings = true;
                            if (httpContext.Request.Headers.ContainsKey("X-allowedToChangeSettings") && bool.TryParse(httpContext.Request.Headers["X-allowedToChangeSettings"].ToString(), out bool _allowedToChangeSettings))
                                allowedToChangeSettings = _allowedToChangeSettings;

                            bool shared = false;
                            if (httpContext.Request.Headers.ContainsKey("X-shared") && bool.TryParse(httpContext.Request.Headers["X-shared"].ToString(), out bool _shared))
                                shared = _shared;

                            int group = 0;
                            if (httpContext.Request.Headers.ContainsKey("X-group") && int.TryParse(httpContext.Request.Headers["X-group"].ToString(), out int _group))
                                group = _group;

                            httpContext.Features.Set(new UserData() 
                            { 
                                id = login, 
                                login = login, 
                                passwd = passwd, 
                                _ip = httpContext.Request.Headers["X-Client-IP"].ToString(), 
                                versionts = httpContext.Request.Headers["X-Versionts"].ToString(), 
                                maxSize = maxSize,
                                maxiptoIsLockHostOrUser = maxiptoIsLockHostOrUser,
                                allowedToChangeSettings = allowedToChangeSettings,
                                shared = shared,
                                group = group,
                                expires = DateTime.Now.AddDays(1) 
                            });

                            return _next(httpContext);
                        }
                        else
                        {
                            if (AppInit.usersDb.FirstOrDefault(i => i.login == login) is UserData _u && _u.passwd == passwd)
                            {
                                if (_u.shared)
                                {
                                    var ushared = _u.Clone();
                                    ushared._ip = clientIp;
                                    ushared.id = $"{login}/{clientIp.Replace(":", "_")}";

                                    httpContext.Features.Set(ushared);
                                    return _next(httpContext);
                                }

                                _u._ip = clientIp;
                                _u.id = login;
                                httpContext.Features.Set(_u);
                                return _next(httpContext);
                            }
                            else if (AppInit.settings.AuthorizationRequired == false)
                            {
                                httpContext.Features.Set(new UserData() { id = login, login = login, passwd = passwd, _ip = clientIp, expires = DateTime.Now.AddDays(1) });
                                return _next(httpContext);
                            }
                            else if (!string.IsNullOrEmpty(AppInit.settings.AuthorizationServerAPI))
                            {
                                return httpContext.Response.WriteAsync($"AuthorizationServerAPI != {clientIp}");
                            }
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
