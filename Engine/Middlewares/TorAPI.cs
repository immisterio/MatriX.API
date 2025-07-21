using MatriX.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace MatriX.API.Engine.Middlewares
{
    public class TorAPI
    {
        #region TorAPI - static
        public static ConcurrentDictionary<string, TorInfo> db = new ConcurrentDictionary<string, TorInfo>();

        static string lsof = string.Empty;

        static string passwd = DateTime.Now.ToBinary().ToString();
        static string Authorization() => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"ts:{passwd}"));

        static TorAPI()
        {
            Directory.CreateDirectory("logs/process");

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                while (!AppInit.Win32NT)
                {
                    if (!AppInit.settings.lsof)
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                    string result = Bash.Run("lsof -i -P -n");
                    if (result != null)
                        lsof = result;
                }
            });
        }
        #endregion

        #region TorAPI
        private readonly RequestDelegate _next;

        IMemoryCache memory;

        IHttpClientFactory httpClientFactory;

        public TorAPI(RequestDelegate next, IMemoryCache memory, IHttpClientFactory httpClientFactory)
        {
            _next = next;
            this.memory = memory;
            this.httpClientFactory = httpClientFactory;
        }
        #endregion

        #region NextPort
        static readonly object portLock = new object();
        static int currentport = 40000;

        static int NextPort(bool useRandom = true)
        {
            lock (portLock)
            {
                if (useRandom)
                {
                    currentport = currentport + Random.Shared.Next(2, 10);
                    if (currentport > 60000)
                        currentport = 40000 + Random.Shared.Next(2, 10);
                }
                else
                {
                    currentport++;
                }

                if (lsof.Contains(currentport.ToString()))
                {
                    for (int i = currentport + 1; i < 60000; i++)
                    {
                        currentport = i;
                        if (!lsof.Contains(currentport.ToString()))
                            break;
                    }
                }

                return currentport;
            }
        }

        static (int ts, int peersListen) goPort()
        {
            return (NextPort(), NextPort(false));
        }
        #endregion

        #region IsPortInUse
        static bool IsPortInUse(int port)
        {
            bool isUsed = false;
            TcpListener listener = null;

            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
            }
            catch (SocketException)
            {
                isUsed = true; // Порт занят
            }
            finally
            {
                // Если слушатель был создан, останавливаем его
                listener?.Stop();
            }

            return isUsed;
        }
        #endregion


        static readonly object newTsLock = new object();

        async public Task InvokeAsync(HttpContext httpContext)
        {
            #region search / torinfo / control
            if (httpContext.Request.Path.Value.StartsWith("/search"))
            {
                await RutorSearch(httpContext).ConfigureAwait(false);
                return;
            }

            var userData = httpContext.Features.Get<UserData>();
            if (userData.login == "service" || httpContext.Request.Path.Value.StartsWith("/torinfo") || httpContext.Request.Path.Value.StartsWith("/control") || httpContext.Request.Path.Value.StartsWith("/userdata"))
            {
                await _next(httpContext);
                return;
            }
            #endregion

            if (string.IsNullOrEmpty(userData.id))
            {
                await httpContext.Response.WriteAsync("user id empty", httpContext.RequestAborted).ConfigureAwait(false);
                return;
            }

            TorInfo info;
            string errorNewToTS = null;
            bool startNewTS = false;

            string inDir = AppInit.appfolder;
            string version = string.IsNullOrEmpty(userData.versionts) ? "latest" : userData.versionts;

            if (version != "latest" && !File.Exists($"{inDir}/TorrServer/{version}"))
                version = "latest";

            #region add newTs
            lock (newTsLock)
            {
                if (!db.TryGetValue(userData.id, out info))
                {
                    startNewTS = true;
                    if (httpContext.Request.Path.Value.StartsWith("/shutdown"))
                    {
                        errorNewToTS = "error";
                    }
                    else
                    {
                        logAction(userData.id, "start run");
                        
                        info = new TorInfo()
                        {
                            user = userData,
                            lastActive = DateTime.Now
                        };

                        if (db.TryAdd(info.user.id, info))
                        {
                            info.taskCompletionSource = new TaskCompletionSource<bool>();
                        }
                        else
                        {
                            errorNewToTS = "error: db.TryAdd(dbKeyOrLogin, info)";
                        }
                    }
                }
            }
            #endregion

            if (errorNewToTS != null)
            {
                logAction(userData.id, errorNewToTS);
                await httpContext.Response.WriteAsync(errorNewToTS, httpContext.RequestAborted).ConfigureAwait(false);
                return;
            }

            if (startNewTS)
            {
                var port = goPort();
                while (IsPortInUse(port.ts) || IsPortInUse(port.peersListen))
                    port = goPort();

                info.port = port.ts;

                #region Создаем папку пользователя
                if (!File.Exists($"{inDir}/sandbox/{info.user.id}/settings.json"))
                {
                    Directory.CreateDirectory($"{inDir}/sandbox/{info.user.id}");
                    File.Copy($"{inDir}/TorrServer/{info.user.default_settings}", $"{inDir}/sandbox/{info.user.id}/settings.json");
                }
                #endregion

                #region Обновляем настройки по умолчанию
                {
                    string default_settings = File.ReadAllText($"{inDir}/TorrServer/{info.user.default_settings}");

                    if (info.user.allowedToChangeSettings)
                    {
                        string user_settings = File.ReadAllText($"{inDir}/sandbox/{info.user.id}/settings.json");

                        string ReaderReadAHead = Regex.Match(user_settings, "\"ReaderReadAHead\":([^,]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                        string PreloadCache = Regex.Match(user_settings, "\"PreloadCache\":([^,]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();

                        default_settings = Regex.Replace(default_settings, "(\"ReaderReadAHead\"):([^,]+)", $"$1:{ReaderReadAHead}", RegexOptions.IgnoreCase);
                        default_settings = Regex.Replace(default_settings, "(\"PreloadCache\"):([^,]+)", $"$1:{PreloadCache}", RegexOptions.IgnoreCase);

                        default_settings = Regex.Replace(default_settings, "(\"PeersListenPort\"):([^,]+)", $"$1:{port.peersListen}", RegexOptions.IgnoreCase);

                        File.WriteAllText($"{inDir}/sandbox/{info.user.id}/settings.json", default_settings);
                    }
                    else if (!File.Exists($"{inDir}/sandbox/{info.user.id}/settings.json"))
                        File.WriteAllText($"{inDir}/sandbox/{info.user.id}/settings.json", default_settings);
                }
                #endregion

                #region Отслеживанием падение процесса
                info.processForExit += (s, e) =>
                {
                    if (info.thread == null)
                        return;

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(info.process_log) && !info.user.shared)
                            File.AppendAllText($"logs/process/{info.user.id}_exit.txt", $"{DateTime.Now}\n\n{info.process_log}\n\n==============================\n\n\n\n");
                    }
                    catch { }

                    info.Dispose();
                    db.TryRemove(info.user.id, out _);
                    logAction(info.user.id, "stop - processForExit");
                };
                #endregion

                #region Запускаем TorrServer
                info.thread = new Thread(() =>
                {
                    try
                    {
                        File.WriteAllText($"{inDir}/sandbox/{info.user.id}/accs.db", $"{{\"ts\":\"{passwd}\"}}");

                        string arguments = $"--httpauth -p {info.port} -d {inDir}/sandbox/{info.user.id}";

                        if (info.user.maxSize > 0)
                            arguments += $" -m {info.user.maxSize}";
                        else if (AppInit.settings.maxSize > 0 && info.user.maxSize != -1)
                            arguments += $" -m {AppInit.settings.maxSize}";

                        if (!string.IsNullOrEmpty(AppInit.settings.tsargs))
                            arguments += $" {AppInit.settings.tsargs.Trim()}";

                        var processInfo = new ProcessStartInfo();
                        processInfo.UseShellExecute = false;
                        processInfo.RedirectStandardError = true;
                        processInfo.RedirectStandardOutput = true;
                        processInfo.FileName = $"{inDir}/TorrServer/{version}";
                        processInfo.Arguments = arguments;

                        var process = Process.Start(processInfo);
                        if (process != null)
                        {
                            process.OutputDataReceived += (sender, args) =>
                            {
                                if (!string.IsNullOrEmpty(args.Data))
                                    info.process_log += args.Data + "\n";
                            };

                            process.ErrorDataReceived += (sender, args) =>
                            {
                                if (!string.IsNullOrEmpty(args.Data))
                                    info.process_log += args.Data + "\n";
                            };

                            info.process = process;
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();
                            process.WaitForExit();
                        }
                        else
                        {
                            info.exception = "process == null";
                        }
                    }
                    catch (Exception ex)
                    {
                        info.exception = ex.ToString();

                        try
                        {
                            File.AppendAllText($"logs/process/{info.user.id}_error.txt", $"{DateTime.Now}\n\n{info.exception}\n\n{info.process_log}\n\n==============================\n\n\n\n");
                        }
                        catch { }
                    }

                    info.OnProcessForExit();
                });

                info.thread.Start();
                #endregion

                #region Проверяем доступность сервера
                if (await CheckPort(info.port, info) == false)
                {
                    info.taskCompletionSource.SetResult(false);
                    info.taskCompletionSource = null;

                    if (info.thread != null)
                        logAction(info.user.id, "stop - checkport");

                    info.Dispose();
                    db.TryRemove(info.user.id, out _);
                    await httpContext.Response.WriteAsync(info?.exception ?? "failed to start", httpContext.RequestAborted).ConfigureAwait(false);
                    return;
                }
                #endregion

                logAction(info.user.id, "start ok");
                info.taskCompletionSource.SetResult(true);
                info.taskCompletionSource = null;
            }

            if (info.taskCompletionSource != null)
            {
                if (await info.taskCompletionSource.Task == false)
                {
                    await httpContext.Response.WriteAsync($"failed to start\n{info.exception}\n\n{info.process_log}", httpContext.RequestAborted).ConfigureAwait(false);
                    return;
                }
            }

            // IP клиента и время последнего запроса
            info.clientIps.Add(info.user._ip);
            info.lastActive = DateTime.Now;

            #region settings
            if (httpContext.Request.Path.Value.StartsWith("/settings"))
            {
                if (httpContext.Request.Method != "POST")
                {
                    httpContext.Response.StatusCode = 404;
                    await httpContext.Response.WriteAsync("404 page not found", httpContext.RequestAborted).ConfigureAwait(false);
                    return;
                }

                using (var client = httpClientFactory.CreateClient("base"))
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.ConnectionClose = false;
                    client.DefaultRequestHeaders.Add("Authorization", Authorization());

                    #region Данные запроса
                    MemoryStream mem = new MemoryStream();
                    await httpContext.Request.Body.CopyToAsync(mem);
                    string requestJson = Encoding.UTF8.GetString(mem.ToArray());
                    #endregion

                    #region Актуальные настройки
                    var response = await client.PostAsync($"http://127.0.0.1:{info.port}/settings", new StringContent("{\"action\":\"get\"}", Encoding.UTF8, "application/json"), httpContext.RequestAborted).ConfigureAwait(false);
                    string settingsJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (requestJson.Trim() == "{\"action\":\"get\"}")
                    {
                        settingsJson = Regex.Replace(settingsJson, "(\"EnableRutorSearch\"):([^,]+)", $"$1:true", RegexOptions.IgnoreCase);

                        httpContext.Response.ContentType = "application/json; charset=utf-8";
                        await httpContext.Response.WriteAsync(settingsJson, httpContext.RequestAborted).ConfigureAwait(false);
                        return;
                    }
                    #endregion

                    if (!info.user.allowedToChangeSettings)
                    {
                        await httpContext.Response.WriteAsync(string.Empty, httpContext.RequestAborted).ConfigureAwait(false);
                        return;
                    }

                    #region Обновляем настройки кеша 
                    string ReaderReadAHead = Regex.Match(requestJson, "\"ReaderReadAHead\":([0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value;
                    string PreloadCache = Regex.Match(requestJson, "\"PreloadCache\":([0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value;

                    settingsJson = Regex.Replace(settingsJson, "\"ReaderReadAHead\":([0-9]+)", $"\"ReaderReadAHead\":{ReaderReadAHead}", RegexOptions.IgnoreCase);
                    settingsJson = Regex.Replace(settingsJson, "\"PreloadCache\":([0-9]+)", $"\"PreloadCache\":{PreloadCache}", RegexOptions.IgnoreCase);
                    settingsJson = "{\"action\":\"set\",\"sets\":" + settingsJson + "}";

                    await client.PostAsync($"http://127.0.0.1:{info.port}/settings", new StringContent(settingsJson, Encoding.UTF8, "application/json"), httpContext.RequestAborted).ConfigureAwait(false);

                    await httpContext.Response.WriteAsync(string.Empty, httpContext.RequestAborted).ConfigureAwait(false);
                    return;
                    #endregion
                }
            }
            #endregion

            if (httpContext.Request.Path.Value.StartsWith("/shutdown"))
            {
                if (info.user.shutdown || AppInit.settings.AuthorizationServerAPI == info.user._ip)
                {
                    info.Dispose();
                    db.TryRemove(info.user.id, out _);
                    logAction(info.user.id, "stop - shutdown");
                }

                await httpContext.Response.WriteAsync("OK", httpContext.RequestAborted).ConfigureAwait(false);
                return;
            }

            #region Отправляем запрос в torrserver
            string clearPath = HttpUtility.UrlDecode(httpContext.Request.Path.Value);
                   clearPath = Regex.Replace(clearPath, "[а-яА-Я]", "z");

            string servUri = $"http://127.0.0.1:{info.port}{Regex.Replace(clearPath + httpContext.Request.QueryString.Value, @"[^\x00-\x7F]", "")}";

            using (var client = httpClientFactory.CreateClient("base"))
            {
                var request = CreateProxyHttpRequest(httpContext, new Uri(servUri));
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, httpContext.RequestAborted).ConfigureAwait(false);

                await CopyProxyHttpResponse(httpContext, response, info).ConfigureAwait(false);
            }
            #endregion
        }


        #region RutorSearch
        async public Task RutorSearch(HttpContext httpContext)
        {
            string login = "rutorsearch";

            if (!db.TryGetValue(login, out TorInfo info))
            {
                string inDir = AppInit.appfolder;
                string version = "latest";

                #region TorInfo
                info = new TorInfo()
                {
                    user = new UserData() { id = login, login = login },
                    port = NextPort(),
                    lastActive = DateTime.Now
                };

                if (!db.TryAdd(login, info))
                {
                    await httpContext.Response.WriteAsync("error: db.TryAdd(dbKeyOrLogin, info)").ConfigureAwait(false);
                    return;
                }

                info.taskCompletionSource = new TaskCompletionSource<bool>();
                #endregion

                if (!File.Exists($"{inDir}/sandbox/{login}/settings.json"))
                {
                    Directory.CreateDirectory($"{inDir}/sandbox/{login}");

                    string default_settings = File.ReadAllText($"{inDir}/TorrServer/default_settings.json");
                    default_settings = Regex.Replace(default_settings, "(\"EnableRutorSearch\"):([^,]+)", $"$1:true", RegexOptions.IgnoreCase);

                    File.WriteAllText($"{inDir}/sandbox/{login}/settings.json", default_settings);
                }

                #region Запускаем TorrServer
                info.thread = new Thread(() =>
                {
                    try
                    {
                        string arguments = $"--httpauth -p {info.port} -d {inDir}/sandbox/{info.user.login}";
                        if (!string.IsNullOrEmpty(AppInit.settings.tsargs))
                            arguments += $" {AppInit.settings.tsargs.Trim()}";

                        File.WriteAllText($"{inDir}/sandbox/{info.user.login}/accs.db", $"{{\"ts\":\"{passwd}\"}}");

                        var processInfo = new ProcessStartInfo();
                        processInfo.UseShellExecute = false;
                        processInfo.RedirectStandardError = true;
                        processInfo.RedirectStandardOutput = true;
                        processInfo.FileName = $"{inDir}/TorrServer/{version}";
                        processInfo.Arguments = arguments;

                        var process = Process.Start(processInfo);
                        if (process != null)
                        {
                            info.process = process;
                            info.process.WaitForExit();
                        }
                        else
                        {
                            info.exception = "process == null";
                        }
                    }
                    catch (Exception ex) { info.exception = ex.ToString(); }

                    info.OnProcessForExit();
                });

                info.thread.Start();
                #endregion

                #region Проверяем доступность сервера
                if (await CheckPort(info.port, info) == false)
                {
                    info.taskCompletionSource.SetResult(false);
                    info.taskCompletionSource = null;

                    string exception = info.exception;
                    logAction(info.user.id, "stop - checkport");

                    info?.Dispose();
                    db.TryRemove(login, out _);
                    await httpContext.Response.WriteAsync(exception ?? "failed to start", httpContext.RequestAborted).ConfigureAwait(false);
                    return;
                }
                #endregion

                info.taskCompletionSource.SetResult(true);
                info.taskCompletionSource = null;

                #region Отслеживанием падение процесса
                info.processForExit += (s, e) =>
                {
                    info?.Dispose();
                    db.TryRemove(login, out _);
                    logAction(info.user.id, "stop - processForExit");
                };
                #endregion
            }

            if (info.taskCompletionSource != null)
            {
                if (await info.taskCompletionSource.Task == false)
                {
                    await httpContext.Response.WriteAsync("failed to start", httpContext.RequestAborted).ConfigureAwait(false);
                    return;
                }
            }

            using (var client = httpClientFactory.CreateClient("base"))
            {
                client.Timeout = TimeSpan.FromSeconds(8);
                client.DefaultRequestHeaders.ConnectionClose = false;
                client.DefaultRequestHeaders.Add("Authorization", Authorization());

                
                var response = await client.GetAsync($"http://127.0.0.1:{info.port}{"/search/" + httpContext.Request.QueryString.Value}", httpContext.RequestAborted).ConfigureAwait(false);
                string result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await httpContext.Response.WriteAsync(result, httpContext.RequestAborted).ConfigureAwait(false);
            }
        }
        #endregion

        #region CheckPort
        async public static Task<bool> CheckPort(int port, TorInfo info = null)
        {
            try
            {
                bool servIsWork = false;
                DateTime endTimeCheckort = DateTime.Now.AddSeconds(AppInit.settings.tsCheckPortTimeout);

                while (true)
                {
                    try
                    {
                        if (DateTime.Now > endTimeCheckort || (info != null && info.thread == null))
                            break;

                        await Task.Delay(50);

                        using (HttpClient client = Startup.httpClientFactory != default ? Startup.httpClientFactory.CreateClient("base") : new HttpClient())
                        {
                            client.Timeout = TimeSpan.FromSeconds(2);

                            var response = await client.GetAsync($"http://127.0.0.1:{port}/echo");
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                string echo = await response.Content.ReadAsStringAsync();
                                if (echo.StartsWith("MatriX."))
                                {
                                    servIsWork = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                return servIsWork;
            }
            catch
            {
                return false;
            }
        }
        #endregion


        #region CreateProxyHttpRequest
        HttpRequestMessage CreateProxyHttpRequest(HttpContext context, Uri uri)
        {
            var request = context.Request;

            var requestMessage = new HttpRequestMessage();
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

            requestMessage.Headers.ConnectionClose = false;
            requestMessage.Headers.Add("Authorization", Authorization());
            requestMessage.Headers.Host = context.Request.Host.Value;// uri.Authority;
            requestMessage.RequestUri = uri;
            requestMessage.Method = new HttpMethod(request.Method);

            return requestMessage;
        }
        #endregion

        #region CopyProxyHttpResponse
        async Task CopyProxyHttpResponse(HttpContext context, HttpResponseMessage responseMessage, TorInfo info)
        {
            var response = context.Response;
            response.StatusCode = (int)responseMessage.StatusCode;
            response.ContentLength = responseMessage.Content.Headers.ContentLength;

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

                        response.Headers.TryAdd(header.Key, header.Value.ToArray());
                    }
                    catch { }
                }
            }
            #endregion

            UpdateHeaders(responseMessage.Headers);
            UpdateHeaders(responseMessage.Content.Headers);

            using (var responseStream = await responseMessage.Content.ReadAsStreamAsync().ConfigureAwait(false))
            {
                if (response.Body == null)
                    throw new ArgumentNullException("destination");

                if (!responseStream.CanRead && !responseStream.CanWrite)
                    throw new ObjectDisposedException("ObjectDisposed_StreamClosed");

                if (!response.Body.CanRead && !response.Body.CanWrite)
                    throw new ObjectDisposedException("ObjectDisposed_StreamClosed");

                if (!responseStream.CanRead)
                    throw new NotSupportedException("NotSupported_UnreadableStream");

                if (!response.Body.CanWrite)
                    throw new NotSupportedException("NotSupported_UnwritableStream");


                byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);

                try
                {
                    int bytesRead;
                    Memory<byte> memoryBuffer = buffer.AsMemory();

                    while ((bytesRead = await responseStream.ReadAsync(memoryBuffer, context.RequestAborted).ConfigureAwait(false)) != 0)
                    {
                        info.lastActive = DateTime.Now;
                        await response.Body.WriteAsync(memoryBuffer.Slice(0, bytesRead), context.RequestAborted).ConfigureAwait(false);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        #endregion


        #region logAction
        public static void logAction(string userid, string msg)
        {
            try
            {
                if (!userid.Contains("/"))
                    File.AppendAllText($"logs/process/{userid}_action.txt", $"{DateTime.Now} | {msg}\n");
            }
            catch { }
        }
        #endregion
    }
}
