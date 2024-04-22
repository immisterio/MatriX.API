using Microsoft.AspNetCore.Mvc;
using MatriX.API.Models;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using MatriX.API.Engine.Middlewares;
using System.Net.Http;
using System;
using System.Threading.Tasks;

namespace MatriX.API.Controllers
{
    public class ControlPanel : Controller
    {
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

			string html_ts = string.Empty;
			foreach (string infile in Directory.GetFiles("TorrServer").OrderByDescending(i => i.EndsWith("latest"))) 
			{
				if (infile.Contains("."))
					continue;

				string name = Path.GetFileName(infile);
				string _checked = ((string.IsNullOrEmpty(userData.versionts) && name == "latest") || name == userData.versionts) ? "checked" : "";

                html_ts += $"<div class=\"flex\"><input type=\"radio\" name=\"ts\" value=\"{name}\" {_checked} /> {name}</div>";
            }

            string html_servers = string.Empty;
			if (AppInit.settings.servers != null)
			{
				foreach (var server in AppInit.settings.servers)
                {
                    string _checked = ((string.IsNullOrEmpty(userData.server) && server.name == "current") || server.host == userData.server) ? "checked" : "";

                    html_servers += $"<div class=\"flex\"><input type=\"radio\" name=\"server\" value=\"{server.host}\" {_checked} /> {server.name}</div>";
				}
			}

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
                userData.server = server;
            }

            if (update)
			{
				System.IO.File.WriteAllText($"{AppInit.settings.appfolder}/usersDb.json", JsonConvert.SerializeObject(AppInit.usersDb, Formatting.Indented));

				if (reload && TorAPI.db.TryGetValue(userData.login, out TorInfo info))
				{
                    info?.Dispose();
                    TorAPI.db.TryRemove(userData.login, out _);
                }

				if (reload && !string.IsNullOrEmpty(userData.server) && !RemoteAPI.serv(userData).Contains("127.0.0.1"))
				{
                    using (var client = new HttpClient())
                    {
                        var request = RemoteAPI.CreateProxyHttpRequest(null, new Uri($"{userData.server}/shutdown"), userData);
                        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    }
                }
            }

            return Content("Настройки сохранены", contentType: "text/html; charset=utf-8");
        }
    }
}
