# Установка на linux
```
curl -s https://raw.githubusercontent.com/immisterio/MatriX.API/master/install.sh | bash
```

# usersDb.json
```
[
  {
    "domainid": "ogurchik", // доступ без авторизации через ogurchik.matrix.io
    "expires": "0001-01-01T00:00:00" // доступ без ограничения по времени
  },
  {
    "login": "ts2", // доступ с авторизацией через matrix.io
    "passwd": "test",
    "expires": "2025-09-01T16:43:00" // доступ до 01.09.2025 16:43
  }
]
```

# Переменные пользователя в usersDb.json
* domainid - домен для доступа без авторизации
* login/passwd - данные пользователя для авторизации
* versionts - версия TorrServer, пользователь может выбрать в matrix.io/control
* server - адрес сервера, пользователь может выбрать в matrix.io/control
* expires - ограничения доступа по времени
* maxiptoIsLockHostOrUser - переопределение maxiptoIsLockHostOrUser из settings.json
* admin - true/false

# Переменные settings.json
* port - порт запуска ip:port
* IPAddressAny - true ip:port / false 127.0.0.1:port
* worknodetominutes - время работы ts с момента последней активности пользователя
* maxiptoIsLockHostOrUser - максимальной количество IP в час на пользователя
* domainid_pattern - поиск domainid для доступа без авторизации, "^([^\\.])\\.matrix.io" / ogurchik.matrix.io
* AuthorizationRequired - true доступ для пользователей usersDb.json / false любой логин и пароль
* onlyRemoteApi - true режим API для master сервера / false master сервер
* servers - список серверов
* AuthorizationServerAPI - ip сервера с которого разрешены API запросы
* interface_network - имя интерфейса для статистики нагрузки на канал (по умолчанию eth0)

# Переменные servers в settings.json
* enable - true/false
* reserve - резервный сервер, участвует только если основные перегружены или недоступны (true/false)
* workinghours - время когда сервер доступен 
* limit - лимиты cpu/ram/network при достижении которых сервер перестает принимать новые запросы
* name - отображаемое имя сервера в matrix.io/control
* host - адрес сервера http://IP:PORT | https://domain.io | etc

# Пример servers в settings.json
```
"enable": true,
"name": "Amsterdam",
"host": "http://IP:8090",
"workinghours": [18,19,20,21,22,23,24], // время работы сервера, указывается в UTC
"limit": {
  "ram": 90, // проценты 1-100
  "cpu": 40, // load average (5 минут)
  "network": { // скорость в MBit/s 
    "all": 800, // общая нагрузка на канал (in/out) 
    "transmitted": 800 // скорость отдачи, используйте вместо all для duplex каналов
  }
}
```

# Пример настройки master, serv1, serv2, reserve

matrix.io - (ip 33.33.33.33)
```
{
  "port": 80,
  "IPAddressAny": true,
  "AuthorizationRequired": true,
  "onlyRemoteApi": true,
  "servers": [{
      "enable": true,
      "name": "Germany",
      "host": "http://45.32.232.1:8090",
      "limit": {
        "ram": 90,
        "cpu": 40,
        "network": {
          "transmitted": 2000 // 2gb на отдачу, канал duplex
        }
      },
      {
        "enable": true,
        "name": "Amsterdam",
        "host": "http://45.32.232.2:8090",
        "workinghours": [18, 19, 20, 21, 22, 23, 24],
        "limit": {
          "ram": 90,
          "cpu": 40,
          "network": {
            "all": 800 // 800 MBit/s (in/out), обычный канал
          }
        }
      },
      {
        "enable": true,
        "reserve": true, // если Germany и Amsterdam недоступны
        "host": "http://45.32.232.3:8090"
      }
    ]
  }
}
```

Germany, Amsterdam, reserve
```
{
  "port": 8090,
  "IPAddressAny": true,
  "worknodetominutes": 4,
  "maxiptoIsLockHostOrUser": 10,
  "AuthorizationServerAPI": "33.33.33.33"
}
```
