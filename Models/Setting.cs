using System.Collections.Generic;

namespace MatriX.API.Models
{
    public class Setting
    {
        public int port { get; set; } = 8090;

        public bool IPAddressAny { get; set; } = true;

        public string appfolder { get; set; }

        public List<Server> servers { get; set; }

        public int worknodetominutes { get; set; } = 5;

        public int maxiptoIsLockHostOrUser { get; set; } = 8;

        public bool AuthorizationRequired { get; set; } = true;

        public string AuthorizationServerAPI { get; set; }

        public HashSet<Known> KnownProxies { get; set; } = new HashSet<Known>();
    }
}
