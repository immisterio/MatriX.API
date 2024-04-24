using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;
using System;
using System.Linq;
using MatriX.API.Engine.Middlewares;
using MatriX.API.Models;
using Microsoft.AspNetCore.Http;

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
                    lastActive = i.lastActive
                };

                if (memoryCache.TryGetValue($"memKeyLocIP:{i.user.login}:{DateTime.Now.Hour}", out HashSet<string> ips))
                    temp.clientIps = ips;

                newinfo.Add(temp);
            }

            return Json(newinfo);
        }


        [Route("top")]
        public string Top() => AppInit.top;

        [Route("xrealip")]
        public string XRealIP() => HttpContext.Connection.RemoteIpAddress.ToString();

        [Route("headers")]
        public ActionResult Headers() => Json(HttpContext.Request.Headers);
    }
}
