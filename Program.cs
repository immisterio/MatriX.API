using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using System.Net;
using System.Text.RegularExpressions;
using MatriX.API.Models;
using System.Threading;
using MatriX.API.Engine.Middlewares;
using System;
using System.Threading.Tasks;

namespace MatriX.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            string appfolder = System.IO.Directory.GetCurrentDirectory();

            foreach (var line in args)
            {
                var g = new Regex("-([^=]+)=([^\n\r]+)").Match(line).Groups;
                string comand = g[1].Value;
                string value = g[2].Value;

                switch (comand)
                {
                    case "d":
                        {
                            // -d=/opt/matrix
                            appfolder = value;
                            break;
                        }
                }
            }

            #region settings.json
            if (System.IO.File.Exists($"{appfolder}/settings.json"))
                AppInit.settings = JsonConvert.DeserializeObject<Setting>(System.IO.File.ReadAllText($"{appfolder}/settings.json"));

            AppInit.settings.appfolder = appfolder;
            #endregion

            #region load whiteip.txt
            if (System.IO.File.Exists($"{appfolder}/whiteip.txt"))
            {
                foreach (string ip in System.IO.File.ReadAllLines($"{appfolder}/whiteip.txt"))
                {
                    if (string.IsNullOrWhiteSpace(ip) || !ip.Contains("."))
                        continue;

                    try
                    {
                        if (ip.Contains("/"))
                        {
                            if (int.TryParse(ip.Split("/")[1], out int prefixLength))
                                AppInit.whiteip.Add(new IPNetwork(IPAddress.Parse(ip.Split("/")[0]), prefixLength));
                        }
                        else
                        {
                            AppInit.whiteip.Add(new IPNetwork(IPAddress.Parse(ip), 0));
                        }
                    }
                    catch { }
                }
            }
            #endregion

            #region check node
            ThreadPool.QueueUserWorkItem(async _ =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(20));

                    try
                    {
                        foreach (var node in TorAPI.db.ToArray())
                        {
                            if (node.Key == "rutorsearch")
                                continue;

                            if (node.Value.countError >= 2 || DateTime.Now.AddMinutes(-AppInit.settings.worknodetominutes) > node.Value.lastActive)
                            {
                                TorAPI.db.TryRemove(node.Key, out TorInfo torInfo);
                                node.Value.Dispose();
                            }
                            else
                            {
                                if (node.Value.lastActive.AddSeconds(10) > DateTime.Now)
                                    continue;

                                if (await TorAPI.CheckPort(node.Value.port) == false)
                                {
                                    node.Value.countError += 1;
                                }
                                else
                                {
                                    node.Value.countError = 0;
                                }
                            }
                        }
                    }
                    catch { }
                }
            });
            #endregion

            CreateHostBuilder(null).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(op => op.Listen(AppInit.settings.IPAddressAny ? IPAddress.Any : IPAddress.Parse("127.0.0.1"), AppInit.settings.port));
                    webBuilder.UseStartup<Startup>();
                });
    }
}
