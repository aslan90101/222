== fetch vpngate ==
fetched https://www.vpngate.net/api/iphone/ bytes=1307041
parsed=97
countries=12 :: AE(1) GB(2) JP(49) KR(27) PA(1) PY(1) RO(1) RU(7) TH(5) TW(1) US(1) VN(1)
proto:port -> tcp:1291=1 tcp:1583=1 tcp:1322=1 tcp:1815=1 tcp:1510=1 udp:11300=1 tcp:1852=1 udp:1438=1 tcp:1511=1 tcp:1490=1 tcp:1323=1 udp:1656=1 tcp:1652=1 tcp:1674=1 tcp:1353=1 tcp:1679=1 tcp:1284=1 tcp:1919=1 tcp:1663=1 tcp:1958=1 tcp:1753=1 tcp:1821=1 udp:26389=1 tcp:1655=1 tcp:1541=1 tcp:1204=1 udp:1195=2 tcp:1313=1 tcp:1723=1 tcp:1588=1 tcp:1362=1 udp:38311=1 tcp:1639=1 tcp:1909=1 tcp:1399=1 tcp:1670=1 tcp:1576=1 tcp:1634=1 udp:1767=1 tcp:1402=1 tcp:1602=1 udp:35713=1 tcp:1964=1 tcp:1348=1 udp:27554=1 udp:1783=1 tcp:1570=1 udp:7055=1 tcp:1496=1 tcp:443=18 udp:5008=1 tcp:1538=1 tcp:1980=1 udp:3768=1 tcp:1999=1 udp:1858=1 tcp:1743=1 tcp:1882=1 tcp:1442=1 tcp:1275=1 tcp:5555=1 tcp:1636=1 tcp:1716=1 tcp:1267=1 tcp:1495=1 udp:1926=1 tcp:1407=1 tcp:1397=1 tcp:1385=1 tcp:1595=2 tcp:1343=1 tcp:995=7
== setup openvpn ==
openvpn+ wintun ready
ip before: {"status":"success","country":"United States","countryCode":"US","city":"Des Moines","query":"172.169.255.144"}
== try candidates ==
candidate vpn695669960 GB tcp 78.147.48.105:1595 ping=0
  vpn695669960-tcp443 => connected=False (9s) 2026-11-08 17:08:14 us=93000   connect_timeout = 8
  vpn695669960-udp443 => connected=False (8s) 2026-11-08 17:08:26 us=156000   connect_timeout = 8
  vpn695669960-udp1194 => connected=False (7s) 2026-11-08 17:08:38 us=187000   connect_timeout = 8
  vpn695669960-orig => connected=False (2s) 2026-11-08 17:08:48 us=203000   connect_timeout = 8
candidate vpn390500314 RU tcp 109.110.51.126:1639 ping=0
  vpn390500314-tcp443 => connected=False (9s) 2026-11-08 17:08:53 us=218000   connect_timeout = 8
  vpn390500314-udp443 => connected=False (8s) 2026-11-08 17:09:05 us=250000   connect_timeout = 8
  vpn390500314-udp1194 => connected=False (7s) 2026-11-08 17:09:16 us=296000   connect_timeout = 8
  vpn390500314-orig => connected=False (8s) 2026-11-08 17:09:26 us=312000   connect_timeout = 8
candidate vpn502921842 RU udp 5.228.117.6:5008 ping=0
  vpn502921842-tcp443 => connected=False (9s) 2026-11-08 17:09:37 us=328000   connect_timeout = 8
  vpn502921842-udp443 => connected=False (8s) 2026-11-08 17:09:49 us=359000   connect_timeout = 8
  vpn502921842-udp1194 => connected=False (8s) 2026-11-08 17:10:00 us=375000   connect_timeout = 8
  vpn502921842-orig => connected=False (8s) 2026-11-08 17:10:11 us=421000   connect_timeout = 8
candidate vpn678436306 JP tcp 121.103.60.56:1348 ping=1
  vpn678436306-tcp443 => connected=False (9s) 2026-11-08 17:10:22 us=453000   connect_timeout = 8
  vpn678436306-udp443 => connected=False (7s) 2026-11-08 17:10:34 us=484000   connect_timeout = 8
  vpn678436306-udp1194 => connected=False (8s) 2026-11-08 17:10:44 us=515000   connect_timeout = 8
  vpn678436306-orig => connected=False (2s) 2026-11-08 17:10:55 us=531000   connect_timeout = 8
candidate vpn948730446 VN udp 103.186.154.240:35713 ping=1
  vpn948730446-tcp443 => connected=False (9s) 2026-11-08 17:11:00 us=546000   connect_timeout = 8
  vpn948730446-udp443 => connected=False (7s) 2026-11-08 17:11:12 us=593000   connect_timeout = 8
  vpn948730446-udp1194 => connected=False (8s) 2026-11-08 17:11:22 us=609000   connect_timeout = 8
  vpn948730446-orig => connected=False (7s) 2026-11-08 17:11:33 us=640000   connect_timeout = 8
candidate vpn427539242 JP tcp 121.85.136.123:1343 ping=1
  vpn427539242-tcp443 => connected=False (9s) 2026-11-08 17:11:43 us=656000   connect_timeout = 8
  vpn427539242-udp443 => connected=False (8s) 2026-11-08 17:11:55 us=671000   connect_timeout = 8
  vpn427539242-udp1194 => connected=False (8s) 2026-11-08 17:12:06 us=703000   connect_timeout = 8
  vpn427539242-orig => connected=False (9s) 2026-11-08 17:12:17 us=734000   connect_timeout = 8
candidate vpn768481862 AE udp 13.140.95.135:38311 ping=1
  vpn768481862-tcp443 => connected=False (1s) 2026-11-08 17:12:29 us=765000   connect_timeout = 8
  vpn768481862-udp443 => connected=False (8s) 2026-11-08 17:12:33 us=781000   connect_timeout = 8
  vpn768481862-udp1194 => connected=False (7s) 2026-11-08 17:12:44 us=812000   connect_timeout = 8
  vpn768481862-orig => connected=False (3s) 2026-11-08 17:12:54 us=843000   connect_timeout = 8
candidate vpn716266014 GB udp 79.127.144.49:3768 ping=1
  vpn716266014-tcp443 => connected=False (1s) 2026-11-08 17:13:00 us=875000   connect_timeout = 8
  vpn716266014-udp443 => connected=False (8s) 2026-11-08 17:13:04 us=890000   connect_timeout = 8
  vpn716266014-udp1194 => connected=False (7s) 2026-11-08 17:13:15 us=937000   connect_timeout = 8
  vpn716266014-orig => connected=False (7s) 2026-11-08 17:13:25 us=953000   connect_timeout = 8
candidate jayporeonvpn01 TW tcp 118.166.2.170:443 ping=2
  jayporeonvpn01-tcp443 => connected=False (2s) 2026-11-08 17:13:35 us=984000   connect_timeout = 8
  jayporeonvpn01-udp443 => connected=False (7s) 2026-11-08 17:13:40 us=984000   connect_timeout = 8
  jayporeonvpn01-udp1194 => connected=False (2s) 2026-11-08 17:13:50 us=15000   connect_timeout = 8
  jayporeonvpn01-orig => connected=False (2s) 2026-11-08 17:13:55 us=15000   connect_timeout = 8
candidate vpn794332876 JP tcp 180.145.56.205:1670 ping=2
  vpn794332876-tcp443 => connected=False (9s) 2026-11-08 17:14:00 us=31000   connect_timeout = 8
  vpn794332876-udp443 => connected=False (8s) 2026-11-08 17:14:12 us=62000   connect_timeout = 8
  vpn794332876-udp1194 => connected=False (8s) 2026-11-08 17:14:23 us=93000   connect_timeout = 8
  vpn794332876-orig => connected=False (9s) 2026-11-08 17:14:34 us=109000   connect_timeout = 8
candidate vpn435394266 JP tcp 121.86.244.13:1495 ping=2
  vpn435394266-tcp443 => connected=False (9s) 2026-11-08 17:14:46 us=140000   connect_timeout = 8
  vpn435394266-udp443 => connected=False (8s) 2026-11-08 17:14:59 us=187000   connect_timeout = 8
  vpn435394266-udp1194 => connected=False (8s) 2026-11-08 17:15:10 us=218000   connect_timeout = 8
  vpn435394266-orig => connected=False (2s) 2026-11-08 17:15:21 us=234000   connect_timeout = 8
candidate vpn593692999 JP tcp 117.108.66.173:1442 ping=3
  vpn593692999-tcp443 => connected=False (9s) 2026-11-08 17:15:26 us=250000   connect_timeout = 8
  vpn593692999-udp443 => connected=False (8s) 2026-11-08 17:15:38 us=265000   connect_timeout = 8
  vpn593692999-udp1194 => connected=False (8s) 2026-11-08 17:15:49 us=281000   connect_timeout = 8
  vpn593692999-orig => connected=False (9s) 2026-11-08 17:16:00 us=328000   connect_timeout = 8
== no working server found ==
== openvpn log dump (last working) ==
--- log-jayporeonvpn01-orig.txt ---
  2026-11-08 17:13:55 us=15000   domain = '[UNDEF]'
  2026-11-08 17:13:55 us=15000   netbios_scope = '[UNDEF]'
  2026-11-08 17:13:55 us=15000   netbios_node_type = 0
  2026-11-08 17:13:55 us=15000   disable_nbt = DISABLED
  2026-11-08 17:13:55 us=15000 OpenVPN 2.6.12 [git:v2.6.12/038a94bae57a446c] Windows [SSL (OpenSSL)] [LZO] [LZ4] [PKCS11] [AEAD] [DCO] built on Jul 18 2024
  2026-11-08 17:13:55 us=15000 Windows version 10.0 (Windows 10 or greater), amd64 executable
  2026-11-08 17:13:55 us=15000 library versions: OpenSSL 3.3.1 4 Jun 2024, LZO 2.10
  2026-11-08 17:13:55 us=15000 DCO version: N/A
  2026-11-08 17:13:55 us=15000 WARNING: No server certificate verification method has been enabled.  See http://openvpn.net/howto.html#mitm for more info.
  2026-11-08 17:13:55 us=15000 Control Channel MTU parms [ mss_fix:0 max_frag:0 tun_mtu:1250 tun_max_mtu:0 headroom:126 payload:1600 tailroom:126 ET:0 ]
  2026-11-08 17:13:55 us=15000 Data Channel MTU parms [ mss_fix:0 max_frag:0 tun_mtu:1500 tun_max_mtu:1600 headroom:136 payload:1768 tailroom:562 ET:0 ]
  2026-11-08 17:13:55 us=15000 TCP/UDP: Preserving recently used remote address: [AF_INET]118.166.2.170:443
  2026-11-08 17:13:55 us=15000 Socket Buffers: R=[131072->131072] S=[131072->131072]
  2026-11-08 17:13:55 us=15000 Attempting to establish TCP connection with [AF_INET]118.166.2.170:443
  2026-11-08 17:13:55 us=203000 TCP connection established with [AF_INET]118.166.2.170:443
  2026-11-08 17:13:55 us=203000 TCPv4_CLIENT link local: (not bound)
  2026-11-08 17:13:55 us=203000 TCPv4_CLIENT link remote: [AF_INET]118.166.2.170:443
  2026-11-08 17:13:55 us=359000 TLS: Initial packet from [AF_INET]118.166.2.170:443, sid=d017f751 3997e89d
  2026-11-08 17:13:55 us=546000 VERIFY OK: depth=3, C=US, O=Internet Security Research Group, CN=ISRG Root X1
  2026-11-08 17:13:55 us=546000 VERIFY OK: depth=2, C=US, O=ISRG, CN=Root YR
  2026-11-08 17:13:55 us=546000 VERIFY OK: depth=1, C=US, O=Let's Encrypt, CN=YR2
  2026-11-08 17:13:55 us=546000 VERIFY OK: depth=0, CN=opengw.net
  2026-11-08 17:13:55 us=718000 Control Channel: TLSv1.3, cipher TLSv1.3 TLS_AES_256_GCM_SHA384, peer certificate: 2048 bits RSA, signature: RSA-SHA256, peer temporary key: 253 bits X25519
  2026-11-08 17:13:55 us=718000 [opengw.net] Peer Connection Initiated with [AF_INET]118.166.2.170:443
  2026-11-08 17:13:55 us=718000 TLS: move_session: dest=TM_ACTIVE src=TM_INITIAL reinit_src=1
  2026-11-08 17:13:55 us=718000 TLS: tls_multi_process: initial untrusted session promoted to trusted
  2026-11-08 17:13:56 us=890000 SENT CONTROL [opengw.net]: 'PUSH_REQUEST' (status=1)
  2026-11-08 17:13:57 us=312000 AUTH: Received control message: AUTH_FAILED
  2026-11-08 17:13:57 us=312000 TCP/UDP: Closing socket
  2026-11-08 17:13:57 us=312000 SIGTERM[soft,auth-failure] received, process exiting
--- log-jayporeonvpn01-tcp443.txt ---
  2026-11-08 17:13:35 us=984000   domain = '[UNDEF]'
  2026-11-08 17:13:35 us=984000   netbios_scope = '[UNDEF]'
  2026-11-08 17:13:35 us=984000   netbios_node_type = 0
  2026-11-08 17:13:35 us=984000   disable_nbt = DISABLED
  2026-11-08 17:13:35 us=984000 OpenVPN 2.6.12 [git:v2.6.12/038a94bae57a446c] Windows [SSL (OpenSSL)] [LZO] [LZ4] [PKCS11] [AEAD] [DCO] built on Jul 18 2024
  2026-11-08 17:13:35 us=984000 Windows version 10.0 (Windows 10 or greater), amd64 executable
  2026-11-08 17:13:35 us=984000 library versions: OpenSSL 3.3.1 4 Jun 2024, LZO 2.10
  2026-11-08 17:13:35 us=984000 DCO version: N/A
  2026-11-08 17:13:35 us=984000 WARNING: No server certificate verification method has been enabled.  See http://openvpn.net/howto.html#mitm for more info.
  2026-11-08 17:13:35 us=984000 Control Channel MTU parms [ mss_fix:0 max_frag:0 tun_mtu:1250 tun_max_mtu:0 headroom:126 payload:1600 tailroom:126 ET:0 ]
  2026-11-08 17:13:35 us=984000 Data Channel MTU parms [ mss_fix:0 max_frag:0 tun_mtu:1500 tun_max_mtu:1600 headroom:136 payload:1768 tailroom:562 ET:0 ]
  2026-11-08 17:13:35 us=984000 TCP/UDP: Preserving recently used remote address: [AF_INET]118.166.2.170:443
  2026-11-08 17:13:35 us=984000 Socket Buffers: R=[131072->131072] S=[131072->131072]
  2026-11-08 17:13:35 us=984000 Attempting to establish TCP connection with [AF_INET]118.166.2.170:443
  2026-11-08 17:13:36 us=156000 TCP connection established with [AF_INET]118.166.2.170:443
  2026-11-08 17:13:36 us=156000 TCPv4_CLIENT link local: (not bound)
  2026-11-08 17:13:36 us=156000 TCPv4_CLIENT link remote: [AF_INET]118.166.2.170:443
  2026-11-08 17:13:36 us=453000 TLS: Initial packet from [AF_INET]118.166.2.170:443, sid=6cb76a22 d2252ea4
  2026-11-08 17:13:36 us=640000 VERIFY OK: depth=3, C=US, O=Internet Security Research Group, CN=ISRG Root X1
  2026-11-08 17:13:36 us=640000 VERIFY OK: depth=2, C=US, O=ISRG, CN=Root YR
  2026-11-08 17:13:36 us=640000 VERIFY OK: depth=1, C=US, O=Let's Encrypt, CN=YR2
  2026-11-08 17:13:36 us=640000 VERIFY OK: depth=0, CN=opengw.net
  2026-11-08 17:13:36 us=796000 Control Channel: TLSv1.3, cipher TLSv1.3 TLS_AES_256_GCM_SHA384, peer certificate: 2048 bits RSA, signature: RSA-SHA256, peer temporary key: 253 bits X25519
  2026-11-08 17:13:36 us=796000 [opengw.net] Peer Connection Initiated with [AF_INET]118.166.2.170:443
  2026-11-08 17:13:36 us=796000 TLS: move_session: dest=TM_ACTIVE src=TM_INITIAL reinit_src=1
  2026-11-08 17:13:36 us=796000 TLS: tls_multi_process: initial untrusted session promoted to trusted
  2026-11-08 17:13:38 us=31000 SENT CONTROL [opengw.net]: 'PUSH_REQUEST' (status=1)
  2026-11-08 17:13:38 us=390000 AUTH: Received control message: AUTH_FAILED
  2026-11-08 17:13:38 us=390000 TCP/UDP: Closing socket
  2026-11-08 17:13:38 us=390000 SIGTERM[soft,auth-failure] received, process exiting
