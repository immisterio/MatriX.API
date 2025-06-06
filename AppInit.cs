using Newtonsoft.Json;
using System.IO;
using System;
using System.Collections.Concurrent;
using MatriX.API.Models;
using System.Net;

namespace MatriX.API
{
    public class AppInit
    {
        public static string appfolder = Directory.GetCurrentDirectory();

        public static string top = string.Empty;

        public static ConcurrentBag<IPNetwork> whiteip = new ConcurrentBag<IPNetwork>();

        #region settings.json
        static (Setting, DateTime) cachesettings = default;
        public static Setting settings
        {
            get
            {
                string path = $"{appfolder}/settings.json";

                if (!File.Exists(path))
                    return new Setting();

                var lastWriteTime = File.GetLastWriteTime(path);

                if (cachesettings.Item2 != lastWriteTime)
                {
                    cachesettings.Item2 = lastWriteTime;
                    cachesettings.Item1 = JsonConvert.DeserializeObject<Setting>(File.ReadAllText(path));
                }

                return cachesettings.Item1;
            }
        }
        #endregion

        #region usersDb.json
        static (ConcurrentBag<UserData>, DateTime) cacheusersDb = default;
        public static ConcurrentBag<UserData> usersDb
        {
            get
            {
                string path = $"{appfolder}/usersDb.json";

                if (!File.Exists(path))
                    return new ConcurrentBag<UserData>();

                var lastWriteTime = File.GetLastWriteTime(path);

                if (cacheusersDb.Item2 != lastWriteTime)
                {
                    cacheusersDb.Item2 = lastWriteTime;
                    cacheusersDb.Item1 = JsonConvert.DeserializeObject<ConcurrentBag<UserData>>(File.ReadAllText(path));
                }

                return cacheusersDb.Item1;
            }
        }
        #endregion


        public static void SaveUsersDb(ConcurrentBag<UserData> users)
        {
            string path = $"{appfolder}/usersDb.json";
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(users, settings));
            cacheusersDb.Item2 = File.GetLastWriteTime(path);
            cacheusersDb.Item1 = users;
        }
    }
}
