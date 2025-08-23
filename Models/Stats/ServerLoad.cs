namespace MatriX.API.Models.Stats
{
    public class ServerLoad
    {
        public int cpu {  get; set; }

        public int memory { get; set; }

        public int received { get; set; }

        public int transmitted { get; set; }
    }
}
