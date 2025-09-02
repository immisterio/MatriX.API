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


        #region admin
        [Route("admin")]
        public ActionResult AdminMain()
        {
            var userData = HttpContext.Features.Get<UserData>();
            if (userData == null || !userData.admin)
                return Content("not admin");

            return Content(System.IO.File.ReadAllText("admin.html"), contentType: "text/html; charset=utf-8");
        }
        #endregion

        #region servers
        [HttpGet]
        [Route("admin/servers")]
        public ActionResult ServersGet()
        {
            var userData = HttpContext.Features.Get<UserData>();
            if (userData == null || !userData.admin)
                return Content("not admin");

            return Json(AppInit.settings.servers);
        }

        [HttpPost]
        [Route("admin/servers")]
        public ActionResult ServersSet([FromBody] Server server)
        {
            var userData = HttpContext.Features.Get<UserData>();
            if (userData == null || !userData.admin)
                return Content("not admin");

            var s = AppInit.settings.servers.FirstOrDefault(i => i.host == server.host);
            if (s == null)
                return Json(new { error = "not found server", server });

            s.name = server.name;
            s.host = server.host;
            s.enable = server.enable;
            s.forced = server.forced;
            s.weight = server.weight;
            s.groups = server.groups;
            s.workinghours = server.workinghours;
            s.geo_hide = server.geo_hide;
            s.limit = server.limit;
            s.limit_hard = server.limit_hard;

            System.IO.File.WriteAllText($"{AppInit.appfolder}/settings.json", JsonConvert.SerializeObject(AppInit.settings, Formatting.Indented, new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            }));

            return Json(s);
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
                    readbytes = StatData.servers.Sum(i => i.stats.readbytes),
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

            return Json(StatData.servers);
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

        #region stats/readbytes-day
        [Route("admin/stats/readbytes-day")]
        public ActionResult ReadBytesDayMasterStat()
        {
            var userData = HttpContext.Features.Get<UserData>();
            if (userData == null || !userData.admin)
                return Content("not admin");

            return Content(JsonConvert.SerializeObject(StatData.ReadBytesToDay, Formatting.Indented), "application/javascript; charset=utf-8");
        }
        #endregion

        #region stats/readbytes-month
        [Route("admin/stats/readbytes-month")]
        public ActionResult ReadBytesMonthMasterStat()
        {
            var userData = HttpContext.Features.Get<UserData>();
            if (userData == null || !userData.admin)
                return Content("not admin");

            var result = new Dictionary<string, Dictionary<string, Dictionary<int, long>>>();
            int daysInMonth = DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month);

            for (int day = 1; day <= daysInMonth; day++)
            {
                string filePath = $"stat/readBytes-{day}.json";
                if (!System.IO.File.Exists(filePath))
                    continue;

                var fileContent = System.IO.File.ReadAllText(filePath);
                var dayData = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Dictionary<int, long>>>>(fileContent);

                foreach (var idEntry in dayData)
                {
                    if (!result.TryGetValue(idEntry.Key, out var servDict))
                    {
                        servDict = new Dictionary<string, Dictionary<int, long>>();
                        result[idEntry.Key] = servDict;
                    }

                    foreach (var servEntry in idEntry.Value)
                    {
                        if (!servDict.TryGetValue(servEntry.Key, out var dayDict))
                        {
                            dayDict = new Dictionary<int, long>();
                            servDict[servEntry.Key] = dayDict;
                        }

                        foreach (var hourEntry in servEntry.Value)
                        {
                            // hourEntry.Key - это час, hourEntry.Value - байты
                            // Используем day как ключ вместо hour
                            dayDict[day] = dayDict.ContainsKey(day) ? dayDict[day] + hourEntry.Value : hourEntry.Value;
                        }
                    }
                }
            }

            return Content(JsonConvert.SerializeObject(result, Formatting.Indented), "application/javascript; charset=utf-8");
        }
        #endregion
    }
}
