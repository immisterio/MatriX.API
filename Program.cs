using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Text.RegularExpressions;
using MatriX.API.Models;
using System.Threading;
using MatriX.API.Engine.Middlewares;
using System;
using System.Threading.Tasks;
using System.Net.Http;
using MatriX.API.Engine;

namespace MatriX.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
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
                            AppInit.appfolder = value;
                            break;
                        }
                }
            }

            Bash.Run($"chmod +x {AppInit.appfolder}/TorrServer/latest");

            #region load whiteip.txt
            if (System.IO.File.Exists($"{AppInit.appfolder}/whiteip.txt"))
            {
                foreach (string ip in System.IO.File.ReadAllLines($"{AppInit.appfolder}/whiteip.txt"))
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
                                if (TorAPI.db.TryRemove(node.Key, out TorInfo torInfo))
                                {
                                    node.Value.Dispose();
                                    TorAPI.logAction(torInfo.user.id, $"stop - timeout | countError: {node.Value.countError} / lastActive: {node.Value.lastActive}");
                                }
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

            #region check servers
            ThreadPool.QueueUserWorkItem(async _ =>
            {
                bool firstwhile = true;

                while (true)
                {
                    await Task.Delay(firstwhile ? TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(1));
                    firstwhile = false;

                    try
                    {
                        if (AppInit.settings.servers == null)
                            continue;

                        foreach (var server in AppInit.settings.servers)
                        {
                            try
                            {
                                if (!server.enable || string.IsNullOrEmpty(server.host))
                                    continue;

                                if (server.host.Contains("127.0.0.1"))
                                {
                                    server.status = 1;
                                    continue;
                                }

                                using (HttpClient client = Startup.httpClientFactory != default ? Startup.httpClientFactory.CreateClient("base") : new HttpClient())
                                {
                                    client.Timeout = TimeSpan.FromSeconds(10);

                                    var response = await client.GetAsync($"{server.host}/echo");
                                    if (response.StatusCode == HttpStatusCode.OK)
                                    {
                                        string echo = await response.Content.ReadAsStringAsync();
                                        server.status = echo.StartsWith("MatriX.") ? 1 : 2;

                                        if (server.status == 1 && server.limit != null)
                                        {
                                            try
                                            {
                                                response = await client.GetAsync($"{server.host}/top");
                                                string top = await response.Content.ReadAsStringAsync();

                                                if (top == null || !top.Contains("mem:"))
                                                    continue;

                                                int.TryParse(Regex.Match(top, "mem: ([0-9]+)").Groups[1].Value, out int mem);
                                                int.TryParse(Regex.Match(top, "cpu: ([0-9]+)").Groups[1].Value, out int cpu);

                                                int.TryParse(Regex.Match(top, "Received: ([0-9]+)").Groups[1].Value, out int received);
                                                if (0 > received)
                                                    received = 0;

                                                int.TryParse(Regex.Match(top, "Transmitted: ([0-9]+)").Groups[1].Value, out int transmitted);
                                                if (0 > transmitted)
                                                    transmitted = 0;

                                                if (server.limit.ram != 0 && mem > server.limit.ram)
                                                {
                                                    server.status = 3;
                                                    continue;
                                                }

                                                if (server.limit.cpu != 0 && cpu > server.limit.cpu)
                                                {
                                                    server.status = 3;
                                                    continue;
                                                }

                                                if (server.limit.network != null)
                                                {
                                                    if (server.limit.network.all != 0)
                                                    {
                                                        if ((received + transmitted) > server.limit.network.all)
                                                        {
                                                            server.status = 3;
                                                            continue;
                                                        }
                                                    }

                                                    if (server.limit.network.transmitted != 0 && transmitted > server.limit.network.transmitted)
                                                    {
                                                        server.status = 3;
                                                        continue;
                                                    }

                                                    if (server.limit.network.received != 0 && received > server.limit.network.received)
                                                    {
                                                        server.status = 3;
                                                        continue;
                                                    }
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                    else
                                    {
                                        server.status = 2;
                                    }
                                }
                            }
                            catch { server.status = 3; }
                        }
                    }
                    catch { }
                }
            });
            #endregion

            #region check top
            ThreadPool.QueueUserWorkItem(async _ =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));

                    try
                    {
                        string top = "mem: " + Bash.Run("free -t | awk '/Mem/{printf(\\\"%.0f\\n\\\", ($3-$6)/$2 * 100)}'"); // процент 1-100
                        top += "cpu: " + Bash.Run("uptime | grep -o 'load average: .*' | awk -F ', ' '{print $2}'");
                        top += Bash.Run("sar -n DEV 1 60 | grep Average | grep " + AppInit.settings.interface_network + " | awk '{print \\\"Received: \\\" $5*8/1024 \\\" Mbit/s, Transmitted: \\\" $6*8/1024 \\\" Mbit/s\\\"}'");

                        AppInit.top = top;
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
