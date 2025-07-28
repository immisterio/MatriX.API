using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace MatriX.API.Models
{
    public class UserData
    {
        /// <summary>
        /// Идентификатор домена пользователя для авторизации по домену.
        /// </summary>
        public string domainid { get; set; }

        /// <summary>
        /// Логин пользователя для аутентификации.
        /// </summary>
        public string login { get; set; }

        /// <summary>
        /// Пароль пользователя для аутентификации.
        /// </summary>
        public string passwd { get; set; }

        /// <summary>
        /// Администратор
        /// </summary>
        public bool admin { get; set; }

        /// <summary>
        /// Группа пользователя
        /// </summary>
        public int group { get; set; }

        /// <summary>
        /// Версия TorrSerever (по умолчанию latest)
        /// </summary>
        public string versionts { get; set; }

        /// <summary>
        /// Путь к файлу настроек по умолчанию
        /// </summary>
        public string default_settings { get; set; } = "default_settings.json";

        /// <summary>
        /// Разрешено ли пользователю менять настройки
        /// </summary>
        public bool allowedToChangeSettings { get; set; } = true;

        /// <summary>
        /// Разрешено ли пользователю выключать TorrSerever
        /// </summary>
        public bool shutdown { get; set; } = true;

        /// <summary>
        /// Признак "общего" пользователя (мультидоступ с разных IP).
        /// </summary>
        public bool shared { get; set; }

        /// <summary>
        /// Максимальный размер файла для просмотра в ts
        /// </summary>
        public long maxSize { get; set; }

        /// <summary>
        /// Максимальное количиство IP-адресов, с которых может подключаться пользователь
        /// </summary>
        public byte maxiptoIsLockHostOrUser { get; set; }

        public byte maxIpToStream { get; set; }

        /// <summary>
        /// Список разрешённых IP-адресов для пользователя
        /// </summary>
        public List<string> whiteip { get; set; }

        /// <summary>
        /// Время истечения действия доступа
        /// </summary>
        public DateTime expires { get; set; }

        /// <summary>
        /// Выбранный сервер 
        /// </summary>
        public string server { get; set; }


        [JsonIgnore]
        public string _ip { get; set; }

        [JsonIgnore]
        public string id { get; set; }

        public UserData Clone()
        {
            return (UserData)MemberwiseClone();
        }
    }
}
