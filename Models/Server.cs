using System.Collections.Generic;

namespace MatriX.API.Models
{
    public class Server
    {
        public bool enable { get; set; }

        public int group { get; set; }

        public int[] groups { get; set; }

        public int weight { get; set; } = 1;

        public int[] workinghours { get; set; }

        public ServerLimit limit { get; set; }

        public ServerLimit limit_hard { get; set; }

        public string name { get; set; }

        public string host { get; set; }

        public List<string> geo_hide { get; set; }


        /// <summary>
        /// 0 - не проверялся
        /// 1 - работает
        /// 2 - отключен - недоступен
        /// 3 - отключен - лимит нагрузки
        /// </summary>
        public int status { get; set; }

        /// <summary>
        /// 0 - доступен
        /// 1 - недоступен (лимит нагрузки)
        /// </summary>
        public int status_hard { get; set; }
    }
}
