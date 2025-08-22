using MatriX.API.Middlewares;
using MatriX.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MatriX.API.Controllers
{
    public class ControlPanel : Controller
    {
        IMemoryCache memoryCache;

        public ControlPanel(IMemoryCache m) {
            memoryCache = m;
        }

        [Route("control")]
        public ActionResult Index()
        {
			if (!AppInit.settings.AuthorizationRequired)
				return Content("AuthorizationRequired");

			var userData = HttpContext.Features.Get<UserData>();
            if (userData == null)
                return Content("not user");

            string html = @"
<!DOCTYPE html>
<html>
<head>
	<title>Настройки</title>
</head>
<body>

<style type=""text/css"">
	* {
	    box-sizing: border-box;
	    outline: none;
	}
	body{
		padding: 40px;
		font-family: sans-serif;
	}
	label{
		display: block;
		font-weight: 700;
		margin-bottom: 8px;
	}
	input,
	select{
		margin: 10px;
		margin-left: 0px;
	}
	button{
		padding: 10px;
	}
	form > * + *{
		margin-top: 30px;
	}
	.flex{
		display: flex;
		align-items: center;
	}
</style>

Доступ активен до <b>{expires}</b><br><br><br>

<form method=""post"" action=""/control/save"" id=""form"">
	<div>
		<label>Версия TorrServer</label>
		{torrserver}
	</div>
	<div>
		<label>Сервер</label>
		{server}
	</div>	
	<button type=""submit"">Сохранить</button>
</form>
</body>
</html>
";

            #region Версия TorrServer
            string html_ts = string.Empty;

			foreach (string infile in Directory.GetFiles("TorrServer").OrderByDescending(i => i.EndsWith("latest"))) 
			{
				if (infile.Contains("."))
					continue;

				string name = Path.GetFileName(infile);
				string _checked = ((string.IsNullOrEmpty(userData.versionts) && name == "latest") || name == userData.versionts) ? "checked" : "";

                html_ts += $"<div class=\"flex\"><input type=\"radio\" name=\"ts\" value=\"{name}\" {_checked} /> {name}</div>";
            }
            #endregion

            #region Сервер
            string html_servers = $"<div class=\"flex\"><input type=\"radio\" name=\"server\" value=\"auto\" {(string.IsNullOrEmpty(userData.server) ? "checked" : "")} /> auto</div>";

            if (AppInit.settings.servers != null)
			{
				string geo = GeoIP2.Country(userData._ip);

                foreach (var server in AppInit.settings.servers)
                {
					if (!server.enable)
						continue;

					if (geo != null && server.geo_hide != null && server.geo_hide.Contains(geo))
						continue;

                    if (server.groups != null ? server.groups.Contains(userData.group) : server.group == userData.group)
					{
						string _checked = server.host == userData.server ? "checked" : "";
						string _status = $"<b style=\"color: crimson;\">{server.status} / {server.status_hard}</b>";

						switch (server.status)
						{
							case 0:
                                _status = "<b style=\"color: chocolate;\">not checked</b>";
                                break;
                            case 1:
                                _status = "<b style=\"color: green;\">work</b>";
                                break;
                            case 2:
                                _status = "<b style=\"color: crimson;\">shutdown</b>";
                                break;
                            case 3:
                                _status = "<b style=\"color: crimson;\">overloaded</b>";
                                if (server.limit_hard != null && server.status_hard != 1)
                                    _status = "<b style=\"color: chocolate;\">hard</b>";
                                break;
                        }

                        html_servers += $"<div class=\"flex\"><input type=\"radio\" name=\"server\" value=\"{server.host}\" {_checked} /> {server.name}&nbsp; - &nbsp;{_status}</div>";
					}
				}
			}
            #endregion

            html = html.Replace("{torrserver}", html_ts)
                       .Replace("{server}", html_servers)
                       .Replace("{expires}", (userData.expires == default ? "unlim" : userData.expires.ToString("dd.MM.yyyy HH:mm")));

            return Content(html, contentType: "text/html; charset=utf-8");
        }



        [Route("control/save")]
        async public Task<ActionResult> Save(string ts, string server)
        {
			if (!AppInit.settings.AuthorizationRequired)
				return Content("AuthorizationRequired");

			var userData = HttpContext.Features.Get<UserData>();
            if (userData == null)
                return Content("not user");

			bool update = false, reload = false;

			if (!string.IsNullOrEmpty(ts))
			{
				update = true;
				reload = true;
                userData.versionts = ts;
			}

            if (!string.IsNullOrEmpty(server))
            {
                update = true;
                userData.server = server == "auto" ? null : server;
            }

			if (update)
			{
				if (userData.shared)
				{
					AppInit.sharedUserToServer.AddOrUpdate(userData.id, userData.server, (key, oldValue) => userData.server);
                    System.IO.File.WriteAllText($"{AppInit.appfolder}/sharedUserToServer.json", JsonConvert.SerializeObject(AppInit.sharedUserToServer, Formatting.Indented));
                }
				else
				{
					System.IO.File.WriteAllText($"{AppInit.appfolder}/usersDb.json", JsonConvert.SerializeObject(AppInit.usersDb, Formatting.Indented));

					if (reload && TorAPI.db.TryGetValue(userData.id, out TorInfo info))
					{
						info?.Dispose();
						TorAPI.db.TryRemove(userData.id, out _);
					}

					if (reload && !string.IsNullOrEmpty(userData.server))
					{
						string serv = RemoteAPI.СurrentServer(userData, memoryCache, false);

                        if (serv != null && !serv.Contains("127.0.0.1"))
                        {
                            using (var client = new HttpClient())
                            {
                                var request = RemoteAPI.CreateProxyHttpRequest(null, new Uri($"{userData.server}/shutdown"), userData, userData.server);
                                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                            }
                        }
					}
				}
			}

            string html = @"
<!DOCTYPE html>
<html>
<head>
	<title>Настройки</title>
</head>
<body>

Настройки сохранены</b><br><br><br>

<form method=""get"" action=""/control"" id=""form"">
	<button type=""submit"">Вернутся назад</button>
</form>
</body>
</html>
";

            return Content(html, contentType: "text/html; charset=utf-8");
        }
    }
}
