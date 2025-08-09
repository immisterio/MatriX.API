using System.Collections.Generic;

namespace MatriX.API.Models
{
    public class Setting
    {
        /// <summary>
        /// Порт сервера
        /// </summary>
        public int port { get; set; } = 8090;

        /// <summary>
        /// Режим прослушивания: true - ip:port / false - 127.0.0.1:port
        /// </summary>
        public bool IPAddressAny { get; set; } = true;

        /// <summary>
        /// Номер группы для серверных настроек
        /// </summary>
        public int group { get; set; }

        /// <summary>
        /// Режим API: true - только API для master сервера / false - master сервер
        /// </summary>
        public bool onlyRemoteApi { get; set; }

        /// <summary>
        /// Разрешить поиск только через Remote API
        /// </summary>
        public bool allowSearchOnlyRemoteApi { get; set; }

        /// <summary>
        /// Список настроек серверов
        /// </summary>
        public List<Server> servers { get; set; }

        /// <summary>
        /// Резервный сервер
        /// </summary>
        public string reserve_server { get; set; }

        /// <summary>
        /// Имя интерфейса для мониторинга трафика на линии (по умолчанию eth0)
        /// </summary>
        public string interface_network { get; set; } = "eth0";

        /// <summary>
        /// Время работы ts с момента последнего обращения пользователя (в минутах)
        /// </summary>
        public int worknodetominutes { get; set; } = 5;

        /// <summary>
        /// Максимальное количество IP на пользователя (любой запрос)
        /// </summary>
        public int maxiptoIsLockHostOrUser { get; set; } = 10;

        /// <summary>
        /// Максимальное количество IP для /stream
        /// </summary>
        public byte maxIpToStream { get; set; } = 5;

        /// <summary>
        /// Максимальный размер торрента
        /// </summary>
        public long maxSize { get; set; }

        public RateLimit rateLimiter { get; set; } = new RateLimit();

        /// <summary>
        /// Шаблон domainid для доступа без авторизации, например "^([^\\.]{8})\\." для ogurchik.matrix.io
        /// </summary>
        public string domainid_pattern { get; set; } = "^([^\\.]{8})\\.";

        public string remoteStream_pattern { get; set; } = "(?<sheme>https?)://slave.(?<server>[^/]+)";

        /// <summary>
        /// API домен для domainid авторизации
        /// </summary>
        public string domainid_api { get; set; }

        /// <summary>
        /// Возвращать ошибку если пользователь не найден
        /// </summary>
        public bool UserNotFoundToError { get; set; } = true;

        /// <summary>
        /// Сообщение об ошибке при ненайденном пользователе
        /// </summary>
        public string UserNotFoundToMessage { get; set; } = "user not found";

        /// <summary>
        /// true - строгая авторизация через usersDb.json / false - любой логин и пароль
        /// </summary>
        public bool AuthorizationRequired { get; set; } = true;

        /// <summary>
        /// IP сервера с которого принимать API запросы
        /// </summary>
        public string AuthorizationServerAPI { get; set; }

        /// <summary>
        /// Пароль по умолчанию
        /// </summary>
        public string defaultPasswd { get; set; } = "ts";

        public string tsargs { get; set; }

        public int tsCheckPortTimeout { get; set; } = 15;

        /// <summary>
        /// Использовать lsof для мониторинга системы
        /// </summary>
        public bool lsof { get; set; }

        /// <summary>
        /// Список известных прокси-серверов
        /// </summary>
        public HashSet<Known> KnownProxies { get; set; } = new HashSet<Known>();


        public Dictionary<int, Setting> groupSetting { get; set; } = new Dictionary<int, Setting>();
    }
}
