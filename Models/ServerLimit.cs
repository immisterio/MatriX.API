namespace MatriX.API.Models
{
    public class ServerLimit
    {
        public int ram { get; set; }

        public int cpu { get; set; }

        public ServerLimitNetwork network { get; set; }
    }
}
