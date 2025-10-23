using MatriX.API.Middlewares;
using MatriX.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MatriX.API.Controllers
{
    public class InfoController : Controller
    {
        #region InfoController
        IMemoryCache memoryCache;

        public InfoController(IMemoryCache m) {
            memoryCache = m;
        }
        #endregion

        #region ReadBytesHour
        [Route("readbytes/hour")]
        public ActionResult ReadBytesHour()
        {
            if (AppInit.settings.IsAuthorizationServerAPI(HttpContext.Connection.RemoteIpAddress.ToString()) == false)
            {
                var userData = HttpContext.Features.Get<UserData>();
                if (userData == null || !userData.admin)
                    return Content("not admin");
            }

            return Json(AppInit.ReadBytesToHour);
        }
        #endregion


        [Route("top")]
        public string Top() => AppInit.top;

        [Route("xrealip")]
        public string XRealIP() => HttpContext.Connection.RemoteIpAddress.ToString();

        [Route("userdata")]
        async public Task<ActionResult> GoUserData() 
        {
            var u = HttpContext.Features.Get<UserData>();
            memoryCache.TryGetValue($"memKeyLocIP:{u.id}:{DateTime.Now.Hour}", out HashSet<string> ips);
            memoryCache.TryGetValue($"memKeyLocIP:stream:{u.id}:{DateTime.Now.Hour}", out HashSet<string> ips_stream);

            TorAPI.db.TryGetValue(u.id, out var tinfo);
            AppInit.ReadBytesToHour.TryGetValue(u.id, out ulong readBytes);

            #region slavedata
            JObject slavedata = null;

            string currentServer = RemoteAPI.СurrentServer(u, memoryCache, false);
            if (!string.IsNullOrEmpty(currentServer) && currentServer.StartsWith("http"))
            {
                try
                {
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(5);
                        var request = RemoteAPI.CreateProxyHttpRequest(null, new Uri($"{currentServer}/userdata"), u, null);
                        var response = await client.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                            slavedata = JsonConvert.DeserializeObject<JObject>(await response.Content.ReadAsStringAsync());
                    }
                }
                catch { }
            }
            #endregion

            #region getActiveStreams
            ConcurrentDictionary<string, DateTime> getActiveStreams()
            {
                if (tinfo?.filteredActiveStreams != null)
                    return tinfo.filteredActiveStreams;

                if (slavedata != null && slavedata.ContainsKey("activeStreams"))
                    return slavedata["activeStreams"].ToObject<ConcurrentDictionary<string, DateTime>>();

                return null;
            }
            #endregion

            return Content(JsonConvert.SerializeObject(new
            {
                u.id,
                readBytes,
                readBytes_slave = slavedata != null ? slavedata.Value<ulong>("readBytes") : 0,
                geo = u.geo ?? GeoIP2.Country(u._ip),
                ip = u._ip,
                ips,
                ips_stream,
                activeStreams = getActiveStreams(),
                server = string.IsNullOrEmpty(u.server) ? "auto" : AppInit.settings.servers.FirstOrDefault(i => i.host != null && i.host.StartsWith(u.server))?.name ?? "auto",
                currentServer = currentServer != null ? AppInit.settings.servers.FirstOrDefault(i => i.host != null && i.host.StartsWith(currentServer))?.name : null,
                AppInit.groupSettings(u.group).maxReadBytesToHour,
                maxiptoIsLockHostOrUser = Math.Max(AppInit.groupSettings(u.group).maxiptoIsLockHostOrUser, u.maxiptoIsLockHostOrUser),
                maxIpToStream = Math.Max(AppInit.groupSettings(u.group).maxIpToStream, u.maxIpToStream),
                maxSize = Math.Max(AppInit.groupSettings(u.group).maxSize, u.maxSize),
                AppInit.groupSettings(u.group).rateLimiter.limitStream,
                u.domainid,
                u.login,
                u.passwd,
                u.admin,
                u.group,
                u.versionts,
                u.default_settings,
                u.allowedToChangeSettings,
                u.shutdown,
                u.shared,
                u.whiteip,
                u.expires
            }, Formatting.Indented), "application/json; charset=utf-8");
        }

        [Route("headers")]
        public ActionResult Headers() => Json(HttpContext.Request.Headers);
    }
}
