using MatriX.API.Models.Stats;

namespace MatriX.API.Models
{
    public class UserServer
    {
        public UserServer(string name, string status, string host, bool @checked, ServerLoad load)
        {
            this.name = name;
            this.status = status;
            this.host = host;
            this.@checked = @checked;

            if (load != null)
                this.load = load;
        }

        public string name { get; set; }

        public string status { get; set; }

        public string host { get; set; }

        public bool @checked { get; set; }

        public ServerLoad load { get; set; } = new ServerLoad();
    }
}
