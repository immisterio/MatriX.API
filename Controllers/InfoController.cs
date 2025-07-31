using MatriX.API.Engine.Middlewares;
using MatriX.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MatriX.API.Controllers
{
    public class InfoController : Controller
    {
        IMemoryCache memoryCache;

        public InfoController(IMemoryCache m)
        {
            memoryCache = m;
        }


        [Route("torinfo")]
        public ActionResult TorInfo()
        {
            var userData = HttpContext.Features.Get<UserData>();
            if (userData == null || !userData.admin)
                return Content("not admin");

            List<TorInfo> newinfo = new List<TorInfo>();

            foreach (var i in TorAPI.db.Select(i => i.Value))
            {
                var temp = new TorInfo()
                {
                    port = i.port,
                    user = i.user,
                    lastActive = i.lastActive,
                    activeStreams = i.activeStreams
                };

                if (memoryCache.TryGetValue($"memKeyLocIP:{i.user.id}:{DateTime.Now.Hour}", out HashSet<string> ips))
                    temp.clientIps = ips;

                newinfo.Add(temp);
            }

            return Json(newinfo);
        }


        [Route("top")]
        public string Top() => AppInit.top;

        [Route("xrealip")]
        public string XRealIP() => HttpContext.Connection.RemoteIpAddress.ToString();

        [Route("userdata")]
        public JsonResult GoUserData() 
        {
            var u = HttpContext.Features.Get<UserData>();
            memoryCache.TryGetValue($"memKeyLocIP:{u.id}:{DateTime.Now.Hour}", out HashSet<string> ips);
            memoryCache.TryGetValue($"memKeyLocIP:stream:{u.id}:{DateTime.Now.Hour}", out HashSet<string> ips_stream);

            var tinfo = TorAPI.db[u.id];

            return Json (new
            {
                u.id,
                ip = u._ip,
                ips,
                ips_stream,
                activeStreams = tinfo.filteredActiveStreams,
                server = string.IsNullOrEmpty(u.server) ? "auto" : AppInit.settings.servers.FirstOrDefault(i => i.host != null && i.host.StartsWith(u.server))?.name ?? "auto",
                maxiptoIsLockHostOrUser = u.maxiptoIsLockHostOrUser > AppInit.settings.maxiptoIsLockHostOrUser ? u.maxiptoIsLockHostOrUser : AppInit.settings.maxiptoIsLockHostOrUser,
                maxIpToStream = u.maxIpToStream > AppInit.settings.maxIpToStream ? u.maxIpToStream : AppInit.settings.maxIpToStream,
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
                u.maxSize,
                u.whiteip,
                u.expires
            });
        }

        [Route("headers")]
        public ActionResult Headers() => Json(HttpContext.Request.Headers);
    }
}
