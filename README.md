# ��������� �� linux
```
curl -s https://raw.githubusercontent.com/immisterio/MatriX.API/master/install.sh | bash
```

# usersDb.json
```
[
  {
    "domainid": "ogurchik", // ������ ��� ����������� ����� ogurchik.matrix.io
    "expires": "0001-01-01T00:00:00" // ������ ��� ����������� �� �������
  },
  {
    "login": "ts2", // ������ � ������������ ����� matrix.io
    "passwd": "test",
    "expires": "2025-09-01T16:43:00" // ������ �� 01.09.2025 16:43
  }
]
```

# ���������� ������������ � usersDb.json
* domainid - ����� ��� ������� ��� �����������
* login/passwd - ������ ������������ ��� �����������
* versionts - ������ TorrServer, ������������ ����� ������� � matrix.io/control
* server - ����� �������, ������������ ����� ������� � matrix.io/control
* expires - ����������� ������� �� �������
* maxiptoIsLockHostOrUser - ��������������� maxiptoIsLockHostOrUser �� settings.json
* admin - true/false

# ���������� settings.json
* port - ���� ������� ip:port
* IPAddressAny - true ip:port / false 127.0.0.1:port
* worknodetominutes - ����� ������ ts � ������� ��������� ���������� ������������
* maxiptoIsLockHostOrUser - ������������ ���������� IP � ��� �� ������������
* domainid_pattern - ����� domainid ��� ������� ��� �����������, "^([^\\.])\\.matrix.io" / ogurchik.matrix.io
* AuthorizationRequired - true ������ ��� ������������� usersDb.json / false ����� ����� � ������
* onlyRemoteApi - true ����� API ��� master ������� / false master ������
* servers - ������ ��������
* AuthorizationServerAPI - ip ������� � �������� ��������� API �������
* interface_network - ��� ���������� ��� ���������� �������� �� ����� (�� ��������� eth0)

# ���������� servers � settings.json
* enable - true/false
* reserve - ��������� ������, ��������� ������ ���� �������� ����������� ��� ���������� (true/false)
* workinghours - ����� ����� ������ �������� 
* limit - ������ cpu/ram/network ��� ���������� ������� ������ ��������� ��������� ����� �������
* name - ������������ ��� ������� � matrix.io/control
* host - ����� ������� http://IP:PORT | https://domain.io | etc

# ������ servers � settings.json
```
"enable": true,
"name": "Amsterdam",
"host": "http://IP:8090",
"workinghours": [18,19,20,21,22,23,24], // ����� ������ �������, ����������� � UTC
"limit": {
  "ram": 90, // �������� 1-100
  "cpu": 40, // load average (5 �����)
  "network": { // �������� � MBit/s 
    "all": 800, // ����� �������� �� ����� (in/out) 
    "transmitted": 800 // �������� ������, ����������� ������ all ��� duplex �������
  }
}
```

# ������ ��������� master, serv1, serv2, reserve

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
          "transmitted": 2000 // 2gb �� ������, ����� duplex
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
            "all": 800 // 800 MBit/s (in/out), ������� �����
          }
        }
      },
      {
        "enable": true,
        "reserve": true, // ���� Germany � Amsterdam ����������
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
