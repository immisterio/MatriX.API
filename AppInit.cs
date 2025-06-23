using MatriX.API.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MatriX.API
{
    public class AppInit
    {
        public static bool Win32NT => Environment.OSVersion.Platform == PlatformID.Win32NT;

        public static string appfolder = Directory.GetCurrentDirectory();

        public static string top = string.Empty;

        public static ConcurrentBag<IPNetwork> whiteip = new ConcurrentBag<IPNetwork>();

        static AppInit()
        {
            #region updateSettings
            void updateSettings()
            {
                string path = $"{appfolder}/settings.json";

                if (!File.Exists(path))
                {
                    if (cachesettings.Item1 == null)
                        cachesettings.Item1 = new Setting();

                    return;
                }

                var lastWriteTime = File.GetLastWriteTime(path);

                if (cachesettings.Item2 != lastWriteTime)
                {
                    cachesettings.Item2 = lastWriteTime;
                    cachesettings.Item1 = JsonConvert.DeserializeObject<Setting>(File.ReadAllText(path));
                }
            }
            #endregion

            #region updateUsers
            void updateUsers()
            {
                string path = $"{appfolder}/usersDb.json";

                if (!File.Exists(path))
                {
                    if (cacheusersDb.Item1 == null)
                        cacheusersDb.Item1 = new ConcurrentBag<UserData>();

                    return;
                }

                var lastWriteTime = File.GetLastWriteTime(path);

                if (cacheusersDb.Item2 != lastWriteTime)
                {
                    cacheusersDb.Item2 = lastWriteTime;
                    cacheusersDb.Item1 = JsonConvert.DeserializeObject<ConcurrentBag<UserData>>(File.ReadAllText(path));
                }
            }
            #endregion

            updateSettings();
            updateUsers();

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    updateSettings();
                    updateUsers();
                }
            });
        }


        #region settings.json
        static (Setting, DateTime) cachesettings = default;

        public static Setting settings => cachesettings.Item1;
        #endregion

        #region usersDb.json
        static (ConcurrentBag<UserData>, DateTime) cacheusersDb = default;

        public static ConcurrentBag<UserData> usersDb => cacheusersDb.Item1;
        #endregion

        #region SaveUsersDb
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
        #endregion
    }
}
