using MatriX.API.Middlewares;
using MatriX.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MatriX.API.Controllers
{
    public class AdminController : Controller
    {
        #region ApiController
        IMemoryCache memoryCache;

        public AdminController(IMemoryCache m)
        {
            memoryCache = m;
        }
        #endregion

        #region UpdateUsersdb
        [HttpPost]
        [Route("api/users/updatedb")]
        public ActionResult UpdateUsersdb([FromBody] List<UserData> updatedUsers)
        {
            var userData = HttpContext.Features.Get<UserData>();
            if (userData == null || !userData.admin)
                return Content("not admin");

            if (updatedUsers == null)
                return BadRequest("No users provided");

            // Получаем текущую базу пользователей
            var currentUsers = AppInit.usersDb;

            // Обновляем только те поля, которые пришли в post запросе
            foreach (var updatedUser in updatedUsers)
            {
                var existingUser = currentUsers.FirstOrDefault(u => (u.domainid != null && u.domainid == updatedUser.domainid) || (u.login != null && u.login == updatedUser.login));
                if (existingUser != null)
                {
                    // Обновляем только непустые/не null поля
                    foreach (var prop in typeof(UserData).GetProperties())
                    {
                        var newValue = prop.GetValue(updatedUser);
                        if (newValue != null && !Equals(newValue, prop.PropertyType.IsValueType ? Activator.CreateInstance(prop.PropertyType) : null))
                        {
                            prop.SetValue(existingUser, newValue);
                        }
                    }
                }
                else
                {
                    // Если пользователь не найден, добавляем его в базу
                    currentUsers.Add(updatedUser);
                }
            }

            // Сохраняем обновленную базу пользователей
            AppInit.SaveUsersDb(currentUsers);

            return Ok();
        }
        #endregion

        #region servers
        [Route("admin/servers")]
        public ActionResult Servers()
        {
            var userData = HttpContext.Features.Get<UserData>();
            if (userData == null || !userData.admin)
                return Content("not admin");

            return Content(JsonConvert.SerializeObject(AppInit.settings.servers, Formatting.Indented), "application/javascript; charset=utf-8");
        }
        #endregion

        #region TorInfo
        [Route("admin/torinfo")]
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
                    activeStreams = i.activeStreams,
                    countError = i.countError
                };

                if (memoryCache.TryGetValue($"memKeyLocIP:{i.user.id}:{DateTime.Now.Hour}", out HashSet<string> ips))
                    temp.clientIps = ips;

                newinfo.Add(temp);
            }

            return Content(JsonConvert.SerializeObject(newinfo, Formatting.Indented), "application/javascript; charset=utf-8");
        }
        #endregion

        #region stats
        [Route("admin/stats")]
        public ActionResult Stats()
        {
            if (AppInit.settings.AuthorizationServerAPI != HttpContext.Connection.RemoteIpAddress.ToString())
            {
                var userData = HttpContext.Features.Get<UserData>();
                if (userData == null || !userData.admin)
                    return Content("not admin");
            }

            if (StatData.servers != null && StatData.servers.Count > 0)
            {
                string GetStringSizeInGB(long readbytes)
                {
                    string[] sizes = { "B", "KB", "MB", "GB", "TB" };

                    if (readbytes == 0)
                        return "0 B";

                    long bytes = Math.Abs(readbytes);
                    int order = (int)(Math.Log(bytes) / Math.Log(1024));
                    double num = Math.Round(bytes / Math.Pow(1024, order), 2);

                    return $"{(readbytes < 0 ? "-" : "")}{num} {sizes[order]}";
                }

                return Content(JsonConvert.SerializeObject(new
                {
                    clients = StatData.servers.Sum(i => i.stats.clients),
                    streams = StatData.servers.Sum(i => i.stats.streams),
                    read = GetStringSizeInGB(StatData.servers.Sum(i => i.stats.readbytes))

                }, Formatting.Indented), "application/javascript; charset=utf-8");
            }
            else
            {
                return Content(JsonConvert.SerializeObject(new
                {
                    clients = TorAPI.db.Count,
                    streams = TorAPI.db.Sum(i => i.Value.filteredActiveStreams.Count),
                    readbytes = AppInit.ReadBytesToHour.Sum(i => (long)i.Value)

                }, Formatting.Indented), "application/javascript; charset=utf-8");
            }
        }
        #endregion

        #region stats/servers
        [Route("admin/stats/servers")]
        public ActionResult ServersStats()
        {
            var userData = HttpContext.Features.Get<UserData>();
            if (userData == null || !userData.admin)
                return Content("not admin");

            return Content(JsonConvert.SerializeObject(StatData.servers, Formatting.Indented), "application/javascript; charset=utf-8");
        }
        #endregion

        #region stats/readbytes
        [Route("admin/stats/readbytes")]
        public ActionResult ReadBytesMasterStat()
        {
            var userData = HttpContext.Features.Get<UserData>();
            if (userData == null || !userData.admin)
                return Content("not admin");

            return Content(JsonConvert.SerializeObject(StatData.ReadBytesToHour, Formatting.Indented), "application/javascript; charset=utf-8");
        }
        #endregion
    }
}
