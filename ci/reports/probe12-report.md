== setup openvpn ==
bin: D:\a\222\222\work12\ovpnx\OpenVPN\bin
tapctl exists: True
== tap driver install (silent) ==
installer exit=0
== try tapctl ==
tapctl create exit=0
tapctl list exit=0
  adapter: vEthernet (nat) | Hyper-V Virtual Ethernet Adapter | Up
  adapter: EndorVPN | TAP-Windows Adapter V9 #2 | Disconnected
  adapter: Ethernet 4 | Mellanox ConnectX-5 Ex Virtual Adapter | Up
  adapter: Ethernet 3 | Microsoft Hyper-V Network Adapter #3 | Up
  adapter: Local Area Connection | TAP-Windows Adapter V9 | Disconnected
== wintun ==
tapctl wintun hwid exit=1
  adapter2: vEthernet (nat) | Hyper-V Virtual Ethernet Adapter | Up
  adapter2: EndorVPN | TAP-Windows Adapter V9 #2 | Disconnected
  adapter2: Ethernet 4 | Mellanox ConnectX-5 Ex Virtual Adapter | Up
  adapter2: Ethernet 3 | Microsoft Hyper-V Network Adapter #3 | Up
  adapter2: Local Area Connection | TAP-Windows Adapter V9 | Disconnected
== vpnbook config ==
ip before: {"status":"success","country":"United States","countryCode":"US","city":"Quincy","query":"20.83.228.240"}
--- wintun : --config D:\a\222\222\work12\us16.ovpn --log D:\a\222\222\work12\wintun.log --verb 4 --auth-user-pass D:\a\222\222\work12\auth.txt --connect-timeout 10 --connect-retry-max 1 --windows-driver wintun
  => False (2s) exit=True
   LOG 2026-11-08 17:31:30 us=125000 OPTIONS IMPORT: route options modified
   LOG 2026-11-08 17:31:30 us=125000 OPTIONS IMPORT: --ip-win32 and/or --dhcp-option options modified
   LOG 2026-11-08 17:31:30 us=125000 OPTIONS IMPORT: tun-mtu set to 1500
   LOG 2026-11-08 17:31:30 us=125000 interactive service msg_channel=0
   LOG 2026-11-08 17:31:30 us=125000 ROUTE_GATEWAY 10.1.0.1/255.255.240.0 I=14 HWADDR=7c:ed:8d:cf:8b:8f
   LOG 2026-11-08 17:31:30 us=125000 open_tun
   LOG 2026-11-08 17:31:30 us=125000 All wintun adapters on this system are currently in use or disabled.
   LOG 2026-11-08 17:31:30 us=125000 Exiting due to fatal error
--- tap : --config D:\a\222\222\work12\us16.ovpn --log D:\a\222\222\work12\tap.log --verb 4 --auth-user-pass D:\a\222\222\work12\auth.txt --connect-timeout 10 --connect-retry-max 1 --windows-driver tap-windows6
  => True (5s) exit=False
  ip after: {"status":"success","country":"United States","countryCode":"US","city":"Oakton","query":"147.135.15.16"}
  speed: 0.4 Mbit/s
  dns: 142.251.152.4,142.251.154.4
