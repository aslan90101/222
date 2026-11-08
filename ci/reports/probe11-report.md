== tcp reachability to vpnbook ==
  us16.vpnbook.com:443 => True
  us16.vpnbook.com:80 => True
  de20.vpnbook.com:443 => True
  de20.vpnbook.com:80 => True
cfg bytes=4421
-- resolved ips --
   us16.vpnbook.com -> 147.135.15.16
   de20.vpnbook.com -> 51.75.145.20
== openvpn setup ==
== attempt us16 tcp443 verbose ==
exit=True code=1
  2026-11-08 17:29:13 us=562000   netbios_node_type = 0
  2026-11-08 17:29:13 us=562000   disable_nbt = DISABLED
  2026-11-08 17:29:13 us=562000 OpenVPN 2.6.12 [git:v2.6.12/038a94bae57a446c] Windows [SSL (OpenSSL)] [LZO] [LZ4] [PKCS11] [AEAD] [DCO] built on Jul 18 2024
  2026-11-08 17:29:13 us=562000 Windows version 10.0 (Windows 10 or greater), amd64 executable
  2026-11-08 17:29:13 us=562000 library versions: OpenSSL 3.3.1 4 Jun 2024, LZO 2.10
  2026-11-08 17:29:13 us=562000 DCO version: N/A
  2026-11-08 17:29:13 us=562000 WARNING: No server certificate verification method has been enabled.  See http://openvpn.net/howto.html#mitm for more info.
  2026-11-08 17:29:13 us=562000 NOTE: --fast-io is disabled since we are running on Windows
  2026-11-08 17:29:13 us=578000 Control Channel MTU parms [ mss_fix:0 max_frag:0 tun_mtu:1250 tun_max_mtu:0 headroom:126 payload:1600 tailroom:126 ET:0 ]
  2026-11-08 17:29:13 us=578000 Data Channel MTU parms [ mss_fix:0 max_frag:0 tun_mtu:1500 tun_max_mtu:1600 headroom:136 payload:1768 tailroom:562 ET:0 ]
  2026-11-08 17:29:13 us=578000 TCP/UDP: Preserving recently used remote address: [AF_INET]147.135.15.16:443
  2026-11-08 17:29:13 us=578000 Socket Buffers: R=[131072->131072] S=[131072->131072]
  2026-11-08 17:29:13 us=578000 Attempting to establish TCP connection with [AF_INET]147.135.15.16:443
  2026-11-08 17:29:13 us=578000 TCP connection established with [AF_INET]147.135.15.16:443
  2026-11-08 17:29:13 us=578000 TCPv4_CLIENT link local: (not bound)
  2026-11-08 17:29:13 us=578000 TCPv4_CLIENT link remote: [AF_INET]147.135.15.16:443
  2026-11-08 17:29:13 us=578000 TCPv4_CLIENT WRITE [14] to [AF_INET]147.135.15.16:443: P_CONTROL_HARD_RESET_CLIENT_V2 kid=0 [ ] pid=0 DATA len=0
  2026-11-08 17:29:15 us=718000 TCPv4_CLIENT WRITE [14] to [AF_INET]147.135.15.16:443: P_CONTROL_HARD_RESET_CLIENT_V2 kid=0 [ ] pid=0 DATA len=0
  2026-11-08 17:29:19 us=953000 TCPv4_CLIENT WRITE [14] to [AF_INET]147.135.15.16:443: P_CONTROL_HARD_RESET_CLIENT_V2 kid=0 [ ] pid=0 DATA len=0
  2026-11-08 17:29:20 us=937000 TCPv4_CLIENT READ [26] from [AF_INET]147.135.15.16:443: P_CONTROL_HARD_RESET_SERVER_V2 kid=0 [ 0 ] pid=0 DATA len=0
  2026-11-08 17:29:20 us=937000 TLS: Initial packet from [AF_INET]147.135.15.16:443, sid=b1f5dd66 e84d8040
  2026-11-08 17:29:20 us=937000 WARNING: this configuration may cache passwords in memory -- use the auth-nocache option to prevent this
  2026-11-08 17:29:20 us=937000 TCPv4_CLIENT WRITE [309] to [AF_INET]147.135.15.16:443: P_CONTROL_V1 kid=0 [ 0 ] pid=1 DATA len=283
  2026-11-08 17:29:20 us=953000 TCPv4_CLIENT READ [22] from [AF_INET]147.135.15.16:443: P_ACK_V1 kid=0 [ 0 ] DATA len=0
  2026-11-08 17:29:20 us=953000 TCPv4_CLIENT READ [22] from [AF_INET]147.135.15.16:443: P_ACK_V1 kid=0 [ 0 ] DATA len=0
  2026-11-08 17:29:20 us=953000 TCPv4_CLIENT READ [1222] from [AF_INET]147.135.15.16:443: P_CONTROL_V1 kid=0 [ 1 0 ] pid=1 DATA len=1192
  2026-11-08 17:29:20 us=953000 TCPv4_CLIENT WRITE [26] to [AF_INET]147.135.15.16:443: P_ACK_V1 kid=0 [ 1 0 ] DATA len=0
  2026-11-08 17:29:20 us=953000 TCPv4_CLIENT READ [1168] from [AF_INET]147.135.15.16:443: P_CONTROL_V1 kid=0 [ 1 0 ] pid=2 DATA len=1138
  2026-11-08 17:29:20 us=953000 VERIFY OK: depth=1, CN=vpnbook.com
  2026-11-08 17:29:20 us=953000 VERIFY OK: depth=0, CN=server.vpnbook.com
  2026-11-08 17:29:20 us=953000 TCPv4_CLIENT WRITE [566] to [AF_INET]147.135.15.16:443: P_CONTROL_V1 kid=0 [ 2 1 0 ] pid=2 DATA len=532
  2026-11-08 17:29:21 us=62000 TCPv4_CLIENT READ [192] from [AF_INET]147.135.15.16:443: P_CONTROL_V1 kid=0 [ 2 1 0 ] pid=3 DATA len=158
  2026-11-08 17:29:21 us=62000 TCPv4_CLIENT WRITE [34] to [AF_INET]147.135.15.16:443: P_ACK_V1 kid=0 [ 3 2 1 0 ] DATA len=0
  2026-11-08 17:29:21 us=62000 TCPv4_CLIENT READ [269] from [AF_INET]147.135.15.16:443: P_CONTROL_V1 kid=0 [ 2 1 0 ] pid=4 DATA len=235
  2026-11-08 17:29:21 us=62000 Control Channel: TLSv1.3, cipher TLSv1.3 TLS_AES_256_GCM_SHA384, peer certificate: 2048 bits RSA, signature: RSA-SHA256, peer temporary key: 253 bits X25519
  2026-11-08 17:29:21 us=62000 [server.vpnbook.com] Peer Connection Initiated with [AF_INET]147.135.15.16:443
  2026-11-08 17:29:21 us=62000 TLS: move_session: dest=TM_ACTIVE src=TM_INITIAL reinit_src=1
  2026-11-08 17:29:21 us=62000 TLS: tls_multi_process: initial untrusted session promoted to trusted
  2026-11-08 17:29:21 us=62000 TCPv4_CLIENT WRITE [34] to [AF_INET]147.135.15.16:443: P_ACK_V1 kid=0 [ 4 3 2 1 ] DATA len=0
  2026-11-08 17:29:22 us=125000 SENT CONTROL [server.vpnbook.com]: 'PUSH_REQUEST' (status=1)
  2026-11-08 17:29:22 us=125000 TCPv4_CLIENT WRITE [73] to [AF_INET]147.135.15.16:443: P_CONTROL_V1 kid=0 [ 4 3 2 1 ] pid=3 DATA len=35
  2026-11-08 17:29:23 us=46000 TCPv4_CLIENT READ [269] from [AF_INET]147.135.15.16:443: P_CONTROL_V1 kid=0 [ 2 1 0 ] pid=4 DATA len=235
  2026-11-08 17:29:23 us=46000 TCPv4_CLIENT WRITE [34] to [AF_INET]147.135.15.16:443: P_ACK_V1 kid=0 [ 4 3 2 1 ] DATA len=0
  2026-11-08 17:29:23 us=46000 TCPv4_CLIENT READ [343] from [AF_INET]147.135.15.16:443: P_CONTROL_V1 kid=0 [ 3 2 1 0 ] pid=5 DATA len=305
  2026-11-08 17:29:23 us=46000 PUSH: Received control message: 'PUSH_REPLY,redirect-gateway def1,dhcp-option DNS 1.1.1.1,dhcp-option DNS 8.8.8.8,block-outside-dns,route 10.102.128.1,topology net30,ping 10,ping-restart 60,ifconfig 10.102.128.178 10.102.128.177,peer-id 9,cipher AES-256-GCM,protocol-flags cc-exit tls-ekm dyn-tls-crypt,tun-mtu 1500'
  2026-11-08 17:29:23 us=46000 WARNING: You have specified redirect-gateway and redirect-private at the same time (or the same option multiple times). This is not well supported and may lead to unexpected results
  2026-11-08 17:29:23 us=46000 OPTIONS IMPORT: --ifconfig/up options modified
  2026-11-08 17:29:23 us=46000 OPTIONS IMPORT: route options modified
  2026-11-08 17:29:23 us=46000 OPTIONS IMPORT: --ip-win32 and/or --dhcp-option options modified
  2026-11-08 17:29:23 us=46000 OPTIONS IMPORT: tun-mtu set to 1500
  2026-11-08 17:29:23 us=46000 interactive service msg_channel=0
  2026-11-08 17:29:23 us=62000 ROUTE_GATEWAY 10.1.0.1/255.255.240.0 I=14 HWADDR=7c:1e:52:9d:ae:0a
  2026-11-08 17:29:23 us=62000 open_tun
  2026-11-08 17:29:23 us=62000 Enumerate drivers in registy: 
  2026-11-08 17:29:23 us=62000 Enumerate device interface lists:
  2026-11-08 17:29:23 us=62000 NetCfgInstanceId: {C09438BE-FC12-40EF-A873-7C552140FE1F}, Device Interface: \\?\VMBUS#{f8615163-df3e-46c5-913f-f2d2f965ed0e}#{7c1e529d-ae0a-7c1e-529d-ae0a7c1e529d}#{cac88484-7515-4c03-82e6-71a87abac361}
  2026-11-08 17:29:23 us=62000 NetCfgInstanceId: {C635E1AB-DBCA-4BFA-B29B-F7C604D05903}, Device Interface: \\?\PCI#VEN_15B3&DEV_1016&SUBSYS_019015B3&REV_80#5&cd07c9b&0&2#{cac88484-7515-4c03-82e6-71a87abac361}
  2026-11-08 17:29:23 us=62000 NetCfgInstanceId: {BED99B7E-830F-47CD-987F-1B744A88A968}, Device Interface: \\?\ROOT#VMS_VSMP#0000#{cac88484-7515-4c03-82e6-71a87abac361}
  2026-11-08 17:29:23 us=62000 There are no TAP-Windows, Wintun or ovpn-dco adapters on this system.  You should be able to create an adapter by using tapctl.exe utility.
  2026-11-08 17:29:23 us=62000 Exiting due to fatal error
--- stderr ---
