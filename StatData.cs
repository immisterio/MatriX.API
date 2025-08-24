using MatriX.API.Models.Stats;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace MatriX.API
{
    public static class StatData
    {
        public static List<ServerHtop> servers = new List<ServerHtop>();

        /// <summary>
        /// id, <serv, bytes>
        /// </summary>
        public static Dictionary<string, Dictionary<string, ulong>> ReadBytesToHour = new Dictionary<string, Dictionary<string, ulong>>();

        /// <summary>
        /// id, <serv, <hour, bytes>>
        /// </summary>
        public static Dictionary<string, Dictionary<string, Dictionary<int, long>>> ReadBytesToDay = new Dictionary<string, Dictionary<string, Dictionary<int, long>>>();

        static StatData()
        {
            try
            {
                if (File.Exists("stat/readBytes.json"))
                    ReadBytesToDay = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Dictionary<int, long>>>>(File.ReadAllText("stat/readBytes.json"));
            }
            catch { }

            // reset stat at 00:00
            var timer = new System.Timers.Timer();
            timer.Elapsed += (s, e) =>
            {
                Directory.CreateDirectory("stat");

                if (DateTime.Now.Hour == 0 && DateTime.Now.Minute == 0)
                {
                    File.WriteAllText($"stat/readBytes-{DateTime.Now.Day-1}.json", JsonConvert.SerializeObject(ReadBytesToDay, Formatting.Indented));
                    ReadBytesToDay.Clear();
                }

                File.WriteAllText("stat/readBytes.json", JsonConvert.SerializeObject(ReadBytesToDay, Formatting.Indented));
            };
            timer.Interval = 60000; // 1 min
            timer.Start();
        }
    }
}
