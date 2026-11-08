== parse vpngate ==
bytes=1307096
parse FAIL: Cannot convert value "-" to type "System.Int32". Error: "The input string '-' was not in a correct format."
== extract openvpn ==
== wintun dll ==
wintun ok
== tap driver ==
tap install exit=0
ip before: {"status":"success","country":"United States","countryCode":"US","city":"San Francisco","query":"13.83.42.66"}
pick: vpn376743751 119.230.197.249 Japan ping=1 speed=483.9Mbps
---- openvpn attempt: wintun ----
pid=7492
connected=False after ~80s
  LOG 2026-11-08 17:00:52 us=156000   show_net_up = DISABLED
  LOG 2026-11-08 17:00:52 us=156000   route_method = 0
  LOG 2026-11-08 17:00:52 us=156000   block_outside_dns = DISABLED
  LOG 2026-11-08 17:00:52 us=156000   ip_win32_defined = DISABLED
  LOG 2026-11-08 17:00:52 us=156000   ip_win32_type = 1
  LOG 2026-11-08 17:00:52 us=156000   dhcp_masq_offset = 0
  LOG 2026-11-08 17:00:52 us=156000   dhcp_lease_time = 31536000
  LOG 2026-11-08 17:00:52 us=156000   tap_sleep = 0
  LOG 2026-11-08 17:00:52 us=156000   dhcp_options = 0x00000000
  LOG 2026-11-08 17:00:52 us=156000   dhcp_renew = DISABLED
  LOG 2026-11-08 17:00:52 us=156000   dhcp_pre_release = DISABLED
  LOG 2026-11-08 17:00:52 us=156000   domain = '[UNDEF]'
  LOG 2026-11-08 17:00:52 us=156000   netbios_scope = '[UNDEF]'
  LOG 2026-11-08 17:00:52 us=156000   netbios_node_type = 0
  LOG 2026-11-08 17:00:52 us=156000   disable_nbt = DISABLED
  LOG 2026-11-08 17:00:52 us=156000 OpenVPN 2.6.12 [git:v2.6.12/038a94bae57a446c] Windows [SSL (OpenSSL)] [LZO] [LZ4] [PKCS11] [AEAD] [DCO] built on Jul 18 2024
  LOG 2026-11-08 17:00:52 us=156000 Windows version 10.0 (Windows 10 or greater), amd64 executable
  LOG 2026-11-08 17:00:52 us=156000 library versions: OpenSSL 3.3.1 4 Jun 2024, LZO 2.10
  LOG 2026-11-08 17:00:52 us=156000 DCO version: N/A
  LOG 2026-11-08 17:00:52 us=156000 WARNING: No server certificate verification method has been enabled.  See http://openvpn.net/howto.html#mitm for more info.
  LOG 2026-11-08 17:00:52 us=156000 Control Channel MTU parms [ mss_fix:0 max_frag:0 tun_mtu:1250 tun_max_mtu:0 headroom:126 payload:1600 tailroom:126 ET:0 ]
  LOG 2026-11-08 17:00:52 us=156000 Data Channel MTU parms [ mss_fix:0 max_frag:0 tun_mtu:1500 tun_max_mtu:1600 headroom:136 payload:1768 tailroom:562 ET:0 ]
  LOG 2026-11-08 17:00:52 us=156000 TCP/UDP: Preserving recently used remote address: [AF_INET]119.230.197.249:1555
  LOG 2026-11-08 17:00:52 us=156000 Socket Buffers: R=[131072->131072] S=[131072->131072]
  LOG 2026-11-08 17:00:52 us=156000 Attempting to establish TCP connection with [AF_INET]119.230.197.249:1555
---- openvpn attempt: tap ----
pid=5836
connected=False after ~80s
  LOG 2026-11-08 17:02:16 us=265000   show_net_up = DISABLED
  LOG 2026-11-08 17:02:16 us=265000   route_method = 0
  LOG 2026-11-08 17:02:16 us=265000   block_outside_dns = DISABLED
  LOG 2026-11-08 17:02:16 us=265000   ip_win32_defined = DISABLED
  LOG 2026-11-08 17:02:16 us=265000   ip_win32_type = 3
  LOG 2026-11-08 17:02:16 us=265000   dhcp_masq_offset = 0
  LOG 2026-11-08 17:02:16 us=265000   dhcp_lease_time = 31536000
  LOG 2026-11-08 17:02:16 us=265000   tap_sleep = 0
  LOG 2026-11-08 17:02:16 us=265000   dhcp_options = 0x00000000
  LOG 2026-11-08 17:02:16 us=265000   dhcp_renew = DISABLED
  LOG 2026-11-08 17:02:16 us=265000   dhcp_pre_release = DISABLED
  LOG 2026-11-08 17:02:16 us=265000   domain = '[UNDEF]'
  LOG 2026-11-08 17:02:16 us=265000   netbios_scope = '[UNDEF]'
  LOG 2026-11-08 17:02:16 us=265000   netbios_node_type = 0
  LOG 2026-11-08 17:02:16 us=265000   disable_nbt = DISABLED
  LOG 2026-11-08 17:02:16 us=265000 OpenVPN 2.6.12 [git:v2.6.12/038a94bae57a446c] Windows [SSL (OpenSSL)] [LZO] [LZ4] [PKCS11] [AEAD] [DCO] built on Jul 18 2024
  LOG 2026-11-08 17:02:16 us=265000 Windows version 10.0 (Windows 10 or greater), amd64 executable
  LOG 2026-11-08 17:02:16 us=265000 library versions: OpenSSL 3.3.1 4 Jun 2024, LZO 2.10
  LOG 2026-11-08 17:02:16 us=265000 DCO version: N/A
  LOG 2026-11-08 17:02:16 us=265000 WARNING: No server certificate verification method has been enabled.  See http://openvpn.net/howto.html#mitm for more info.
  LOG 2026-11-08 17:02:16 us=265000 Control Channel MTU parms [ mss_fix:0 max_frag:0 tun_mtu:1250 tun_max_mtu:0 headroom:126 payload:1600 tailroom:126 ET:0 ]
  LOG 2026-11-08 17:02:16 us=265000 Data Channel MTU parms [ mss_fix:0 max_frag:0 tun_mtu:1500 tun_max_mtu:1600 headroom:136 payload:1768 tailroom:562 ET:0 ]
  LOG 2026-11-08 17:02:16 us=265000 TCP/UDP: Preserving recently used remote address: [AF_INET]119.230.197.249:1555
  LOG 2026-11-08 17:02:16 us=265000 Socket Buffers: R=[131072->131072] S=[131072->131072]
  LOG 2026-11-08 17:02:16 us=265000 Attempting to establish TCP connection with [AF_INET]119.230.197.249:1555
---- openvpn attempt: default ----
pid=744
connected=False after ~80s
  LOG 2026-11-08 17:03:40 us=421000   show_net_up = DISABLED
  LOG 2026-11-08 17:03:40 us=421000   route_method = 0
  LOG 2026-11-08 17:03:40 us=421000   block_outside_dns = DISABLED
  LOG 2026-11-08 17:03:40 us=421000   ip_win32_defined = DISABLED
  LOG 2026-11-08 17:03:40 us=421000   ip_win32_type = 3
  LOG 2026-11-08 17:03:40 us=421000   dhcp_masq_offset = 0
  LOG 2026-11-08 17:03:40 us=421000   dhcp_lease_time = 31536000
  LOG 2026-11-08 17:03:40 us=421000   tap_sleep = 0
  LOG 2026-11-08 17:03:40 us=421000   dhcp_options = 0x00000000
  LOG 2026-11-08 17:03:40 us=421000   dhcp_renew = DISABLED
  LOG 2026-11-08 17:03:40 us=421000   dhcp_pre_release = DISABLED
  LOG 2026-11-08 17:03:40 us=421000   domain = '[UNDEF]'
  LOG 2026-11-08 17:03:40 us=421000   netbios_scope = '[UNDEF]'
  LOG 2026-11-08 17:03:40 us=421000   netbios_node_type = 0
  LOG 2026-11-08 17:03:40 us=421000   disable_nbt = DISABLED
  LOG 2026-11-08 17:03:40 us=421000 OpenVPN 2.6.12 [git:v2.6.12/038a94bae57a446c] Windows [SSL (OpenSSL)] [LZO] [LZ4] [PKCS11] [AEAD] [DCO] built on Jul 18 2024
  LOG 2026-11-08 17:03:40 us=421000 Windows version 10.0 (Windows 10 or greater), amd64 executable
  LOG 2026-11-08 17:03:40 us=421000 library versions: OpenSSL 3.3.1 4 Jun 2024, LZO 2.10
  LOG 2026-11-08 17:03:40 us=421000 DCO version: N/A
  LOG 2026-11-08 17:03:40 us=421000 WARNING: No server certificate verification method has been enabled.  See http://openvpn.net/howto.html#mitm for more info.
  LOG 2026-11-08 17:03:40 us=421000 Control Channel MTU parms [ mss_fix:0 max_frag:0 tun_mtu:1250 tun_max_mtu:0 headroom:126 payload:1600 tailroom:126 ET:0 ]
  LOG 2026-11-08 17:03:40 us=421000 Data Channel MTU parms [ mss_fix:0 max_frag:0 tun_mtu:1500 tun_max_mtu:1600 headroom:136 payload:1768 tailroom:562 ET:0 ]
  LOG 2026-11-08 17:03:40 us=421000 TCP/UDP: Preserving recently used remote address: [AF_INET]119.230.197.249:1555
  LOG 2026-11-08 17:03:40 us=421000 Socket Buffers: R=[131072->131072] S=[131072->131072]
  LOG 2026-11-08 17:03:40 us=421000 Attempting to establish TCP connection with [AF_INET]119.230.197.249:1555
== killswitch rules ==
  Ok.
  
rule present: True enabled=True
ip now (should fail): ERR No such host is known. (ip-api.com:80)
  
  Deleted 1 rule(s).
  Ok.
  
ip after restore: {"status":"success","country":"United States","countryCode":"US","city":"San Francisco","query":"13.83.42.66"}
