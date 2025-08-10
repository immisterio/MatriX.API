using MatriX.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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

            if (httpContext.Request.Path.Value.StartsWith("/favicon.ico"))
                return httpContext.Response.SendFileAsync("favicon.ico");

            if (!string.IsNullOrEmpty(AppInit.settings.domainid_pattern) && httpContext.Request.Host.Value != AppInit.settings.domainid_api && AppInit.settings.AuthorizationServerAPI != clientIp)
            {
                #region Авторизация по домену
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

                            if (AppInit.sharedUserToServer.TryGetValue(ushared.id, out string serv))
                                ushared.server = serv;

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
                }

                if (httpContext.Request.Path.Value.StartsWith("/echo"))
                    return httpContext.Response.WriteAsync("MatriX.API");

                httpContext.Response.StatusCode = 403;
                httpContext.Response.ContentType = "text/plain; charset=utf-8";
                return httpContext.Response.WriteAsync(AppInit.settings.UserNotFoundToMessage);
                #endregion
            }
            else 
            {
                #region Авторизация по логину и паролю
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

                                byte maxIpToStream = 0;
                                if (httpContext.Request.Headers.ContainsKey("X-maxIpToStream") && byte.TryParse(httpContext.Request.Headers["X-maxIpToStream"].ToString(), out byte _maxIpToStream))
                                    maxIpToStream = _maxIpToStream;

                                bool allowedToChangeSettings = true;
                                if (httpContext.Request.Headers.ContainsKey("X-allowedToChangeSettings") && bool.TryParse(httpContext.Request.Headers["X-allowedToChangeSettings"].ToString(), out bool _allowedToChangeSettings))
                                    allowedToChangeSettings = _allowedToChangeSettings;

                                bool shared = false;
                                if (httpContext.Request.Headers.ContainsKey("X-shared") && bool.TryParse(httpContext.Request.Headers["X-shared"].ToString(), out bool _shared))
                                    shared = _shared;

                                bool shutdown = false;
                                if (httpContext.Request.Headers.ContainsKey("X-shutdown") && bool.TryParse(httpContext.Request.Headers["X-shutdown"].ToString(), out bool _shutdown))
                                    shutdown = _shutdown;

                                int group = 0;
                                if (httpContext.Request.Headers.ContainsKey("X-group") && int.TryParse(httpContext.Request.Headers["X-group"].ToString(), out int _group))
                                    group = _group;

                                string xip = httpContext.Request.Headers["X-Client-IP"].ToString();

                                httpContext.Features.Set(new UserData()
                                {
                                    id = shared ? $"{login}/{xip.Replace(":", "_")}" : login,
                                    login = login,
                                    passwd = passwd,
                                    _ip = xip,
                                    versionts = httpContext.Request.Headers["X-Versionts"].ToString(),
                                    maxSize = maxSize,
                                    maxiptoIsLockHostOrUser = maxiptoIsLockHostOrUser,
                                    maxIpToStream = maxIpToStream,
                                    allowedToChangeSettings = allowedToChangeSettings,
                                    shared = shared,
                                    group = group,
                                    expires = DateTime.Now.AddDays(1),
                                    shutdown = shutdown
                                });

                                return _next(httpContext);
                            }
                            else
                            {
                                if (AppInit.usersDb.FirstOrDefault(i => i.login == login || i.domainid == login) is UserData _u && passwd == (_u.passwd ?? AppInit.settings.defaultPasswd))
                                {
                                    if (_u.shared)
                                    {
                                        var ushared = _u.Clone();
                                        ushared._ip = clientIp;
                                        ushared.id = $"{login}/{clientIp.Replace(":", "_")}";

                                        if (AppInit.sharedUserToServer.TryGetValue(ushared.id, out string serv))
                                            ushared.server = serv;

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
                                    httpContext.Response.ContentType = "text/plain; charset=utf-8";
                                    return httpContext.Response.WriteAsync($"Указанный IP в AuthorizationServerAPI для {httpContext.Request.Host.Value} не совпадает с {clientIp}");
                                }
                            }
                        }
                    }
                    catch (Exception ex) 
                    {
                        return httpContext.Response.WriteAsync(ex.ToString());
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

                #region download
                if (httpContext.Request.Path.Value.StartsWith("/download/"))
                {
                    httpContext.Features.Set(new UserData()
                    {
                        id = "default",
                        login = "default",
                        passwd = "default",
                        _ip = "127.0.0.1",
                        expires = DateTime.Now.AddDays(1)
                    });

                    return _next(httpContext);
                }
                #endregion

                if (httpContext.Request.Path.Value.StartsWith("/echo"))
                    return httpContext.Response.WriteAsync("MatriX.API");

                httpContext.Response.StatusCode = 401;
                httpContext.Response.Headers.Append("Www-Authenticate", "Basic realm=Authorization Required");
                return Task.CompletedTask;
            }
        }
    }
}
