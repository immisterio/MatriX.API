using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace MatriX.API.Models
{
    public class UserData
    {
        public string domainid { get; set; }


        public string login { get; set; }

        public string passwd { get; set; }

        public bool admin { get; set; }

        public string versionts { get; set; }

        public bool allowedToChangeSettings { get; set; } = true;

        public long maxSize { get; set; }


        public byte maxiptoIsLockHostOrUser { get; set; }

        public List<string> whiteip { get; set; }

        public DateTime expires { get; set; }


        public string server { get; set; }


        [JsonIgnore]
        public string _ip { get; set; }


        [JsonIgnore]
        public string id { get; set; }
    }
}
