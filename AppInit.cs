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
        public static Setting settings = new Setting();

        public static ConcurrentBag<IPNetwork> whiteip = new ConcurrentBag<IPNetwork>();


        static (ConcurrentBag<UserData>, DateTime) cacheusersDb = default;
        public static ConcurrentBag<UserData> usersDb
        {
            get
            {
                string path = $"{settings.appfolder}/usersDb.json";

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
    }
}
