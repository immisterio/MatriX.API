namespace MatriX.API.Models
{
    public class Server
    {
        public bool enable { get; set; }

        public int group { get; set; }

        public bool reserve { get; set; }

        public int[] workinghours { get; set; }

        public ServerLimit limit { get; set; }

        public string name { get; set; }

        public string host { get; set; }


        /// <summary>
        /// 0 - не проверялся
        /// 1 - работает
        /// 2 - отключен - недоступен
        /// 3 - отключен - лимит нагрузки
        /// </summary>
        public int status { get; set; }
    }
}
