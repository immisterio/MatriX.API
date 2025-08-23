using MatriX.API.Models.Stats;
using System.Collections.Generic;

namespace MatriX.API
{
    public static class StatData
    {
        public static List<ServerHtop> servers = new List<ServerHtop>();

        public static Dictionary<string, Dictionary<string, ulong>> ReadBytesToHour = new Dictionary<string, Dictionary<string, ulong>>();
    }
}
