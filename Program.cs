using MatriX.API.Middlewares;
using MatriX.API.Models;
using MatriX.API.Models.Stats;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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

            ThreadPool.SetMinThreads(4096, 1024);

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
                                node.Value.Dispose();
                                TorAPI.db.TryRemove(node.Key, out TorInfo torInfo);
                                TorAPI.logAction(torInfo.user.id, $"stop - timeout | countError: {node.Value.countError} / lastActive: {node.Value.lastActive}");
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
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));

                    try
                    {
                        if (AppInit.settings.servers == null)
                            continue;

                        var currentTime = DateTime.Now;
                        var servers_stats = new List<ServerHtop>();
                        ConcurrentDictionary<string, ulong> readBytesToHour = null;
                        var stats_readBytesToHour = new Dictionary<string, Dictionary<string, ulong>>();

                        foreach (var server in AppInit.settings.servers)
                        {
                            var servhtop = new ServerHtop();

                            try
                            {
                                if (string.IsNullOrEmpty(server.host))
                                    continue;

                                if (!server.enable)
                                {
                                    servhtop.enable = false;
                                    servhtop.name = server.name;
                                    servhtop.host = server.host;
                                    servhtop.checkTime = DateTime.Now;
                                    servhtop.group = server.group;
                                    servhtop.groups = server.groups;
                                    servhtop.workinghours = server.workinghours;
                                    servers_stats.Add(servhtop);
                                    continue;
                                }

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
                                        int status = echo.StartsWith("MatriX.") ? 1 : 2;
                                        int status_hard = 0;

                                        #region update readbytes
                                        if (status == 1)
                                        {
                                            if (readBytesToHour == null)
                                                readBytesToHour = new ConcurrentDictionary<string, ulong>();

                                            try
                                            {
                                                response = await client.GetAsync($"{server.host}/readbytes/hour");
                                                string rbth = await response.Content.ReadAsStringAsync();

                                                foreach (var kvp in JsonConvert.DeserializeObject<ConcurrentDictionary<string, ulong>>(rbth))
                                                {
                                                    if (stats_readBytesToHour.TryGetValue(kvp.Key, out var _val))
                                                    {
                                                        if (_val.TryGetValue(server.host, out ulong _h))
                                                        {
                                                            _val[server.host] = kvp.Value;
                                                        }
                                                        else
                                                        {
                                                            stats_readBytesToHour[kvp.Key].TryAdd(server.host, kvp.Value);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        stats_readBytesToHour.TryAdd(kvp.Key, new Dictionary<string, ulong>() { [server.host] = kvp.Value });
                                                    }

                                                    ulong sum = (ulong)stats_readBytesToHour[kvp.Key].Sum(i => (long)i.Value);
                                                    readBytesToHour.AddOrUpdate(kvp.Key, sum, (k, v) => sum);
                                                }
                                            }
                                            catch { }
                                        }
                                        #endregion

                                        #region update stat
                                        if (status == 1)
                                        {
                                            try
                                            {
                                                response = await client.GetAsync($"{server.host}/admin/stats");
                                                string rbth = await response.Content.ReadAsStringAsync();
                                                servhtop.stats = JsonConvert.DeserializeObject<ServerStat>(rbth);
                                            }
                                            catch { }
                                        }
                                        #endregion

                                        if (status == 1)
                                        {
                                            try
                                            {
                                                response = await client.GetAsync($"{server.host}/top");
                                                string top = await response.Content.ReadAsStringAsync();

                                                if (top != null && top.Contains("mem:"))
                                                {
                                                    int.TryParse(Regex.Match(top, "mem: ([0-9]+)").Groups[1].Value, out int mem);
                                                    int.TryParse(Regex.Match(top, "cpu: ([0-9]+)").Groups[1].Value, out int cpu);

                                                    int.TryParse(Regex.Match(top, "Received: ([0-9]+)").Groups[1].Value, out int received);
                                                    if (0 > received)
                                                        received = 0;

                                                    int.TryParse(Regex.Match(top, "Transmitted: ([0-9]+)").Groups[1].Value, out int transmitted);
                                                    if (0 > transmitted)
                                                        transmitted = 0;

                                                    servhtop.load.cpu = cpu;
                                                    servhtop.load.memory = mem;
                                                    servhtop.load.received = received;
                                                    servhtop.load.transmitted = transmitted;

                                                    if (server.limit != null)
                                                    {
                                                        #region ram
                                                        if (server.limit.ram != 0 && mem > server.limit.ram)
                                                            status = 3;

                                                        if (server.limit_hard != null)
                                                        {
                                                            if (server.limit_hard.ram != 0 && mem > server.limit_hard.ram)
                                                                status_hard = 1;
                                                        }
                                                        #endregion

                                                        #region cpu
                                                        if (server.limit.cpu != 0 && cpu > server.limit.cpu)
                                                            status = 3;

                                                        if (server.limit_hard != null)
                                                        {
                                                            if (server.limit_hard.cpu != 0 && cpu > server.limit_hard.cpu)
                                                                status_hard = 1;
                                                        }
                                                        #endregion

                                                        #region network
                                                        if (server.limit.network != null)
                                                        {
                                                            if (server.limit.network.all != 0)
                                                            {
                                                                if ((received + transmitted) > server.limit.network.all)
                                                                    status = 3;
                                                            }

                                                            if (server.limit.network.transmitted != 0 && transmitted > server.limit.network.transmitted)
                                                                status = 3;

                                                            if (server.limit.network.received != 0 && received > server.limit.network.received)
                                                                status = 3;
                                                        }
                                                        #endregion

                                                        #region network_hard
                                                        if (server.limit_hard != null && server.limit_hard.network != null)
                                                        {
                                                            if (server.limit_hard.network.all != 0)
                                                            {
                                                                if ((received + transmitted) > server.limit_hard.network.all)
                                                                    status_hard = 1;
                                                            }

                                                            if (server.limit_hard.network.transmitted != 0 && transmitted > server.limit_hard.network.transmitted)
                                                                status_hard = 1;

                                                            if (server.limit_hard.network.received != 0 && received > server.limit_hard.network.received)
                                                                status_hard = 1;
                                                        }
                                                        #endregion
                                                    }
                                                }
                                            }
                                            catch { }
                                        }

                                        server.status = status;
                                        server.status_hard = status_hard;
                                        servhtop.status_hard = server.status_hard;
                                    }
                                    else
                                    {
                                        server.status = 2;
                                    }
                                }
                            }
                            catch { server.status = 2; }

                            servhtop.enable = server.enable;
                            servhtop.name = server.name;
                            servhtop.host = server.host;
                            servhtop.checkTime = DateTime.Now;
                            servhtop.status = server.status;
                            servhtop.group = server.group;
                            servhtop.groups = server.groups;
                            servhtop.workinghours = server.workinghours;
                            servers_stats.Add(servhtop);
                        }

                        if (readBytesToHour != null)
                            AppInit.ReadBytesToHour = readBytesToHour;

                        StatData.servers = servers_stats;
                        StatData.ReadBytesToHour = stats_readBytesToHour;

                        #region ReadBytesToDay
                        if (currentTime.Minute >= 5)
                        {
                            foreach (var idEntry in stats_readBytesToHour)
                            {
                                string id = idEntry.Key;
                                foreach (var servEntry in idEntry.Value)
                                {
                                    string serv = servEntry.Key;
                                    ulong bytes = servEntry.Value;

                                    if (!StatData.ReadBytesToDay.TryGetValue(id, out var servDict))
                                    {
                                        servDict = new Dictionary<string, Dictionary<int, long>>();
                                        StatData.ReadBytesToDay[id] = servDict;
                                    }

                                    if (!servDict.TryGetValue(serv, out var hourDict))
                                    {
                                        hourDict = new Dictionary<int, long>();
                                        servDict[serv] = hourDict;
                                    }

                                    hourDict[currentTime.Hour] = (long)bytes;
                                }
                            }
                        }
                        #endregion
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
                        string top = "mem: " + Bash.Run("free -t | grep \"Mem:\" | awk '{printf \\\"%.0f\\n\\\", $3/$2*100}'"); // процент занятой RAM | 1-100
                        top += "cpu: " + Bash.Run("uptime | grep -o 'load average: .*' | awk -F ', ' '{print $2}'");
                        top += Bash.Run($"sar -n DEV 1 60 | grep Average | egrep [[:space:]]{AppInit.settings.interface_network}[[:space:]] | awk '{{print \\\"Received: \\\" $5*8/1024 \\\" Mbit/s, Transmitted: \\\" $6*8/1024 \\\" Mbit/s\\\"}}'");

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
