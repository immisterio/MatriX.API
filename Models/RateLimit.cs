namespace MatriX.API.Models
{
    public class RateLimit
    {
        /// <summary>
        /// Timeout in seconds
        /// </summary>
        public int timeout { get; set; } = 10;

        /// <summary>
        /// Максимальное количество одновременных видео потоков на пользователя
        /// </summary>
        public int limitStream { get; set; } = 4;

        public string urlVideoError { get; set; } = "/error_ratelimit.mp4";
    }
}
