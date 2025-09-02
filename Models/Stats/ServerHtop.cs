using System;

namespace MatriX.API.Models.Stats
{
    public class ServerHtop
    {
        public DateTime checkTime { get; set; }

        public bool enable { get; set; }

        public string name { get; set; }

        public string host { get; set; }

        public int group { get; set; }

        public int[] groups { get; set; }

        public int[] workinghours { get; set; }

        public int status {  get; set; }

        public int status_hard { get; set; }

        public ServerLoad load { get; set; } = new ServerLoad();

        public ServerStat stats { get; set; } = new ServerStat();
    }
}
