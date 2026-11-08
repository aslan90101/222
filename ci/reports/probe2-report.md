== download openvpn msi ==
openvpn.exe exists: True
--version: OpenVPN 2.6.12 [git:v2.6.12/038a94bae57a446c] Windows [SSL (OpenSSL)] [LZO] [LZ4] [PKCS11] [AEAD] [DCO] built on Jul 18 2024 | library versions: OpenSSL 3.3.1 4 Jun 2024, LZO 2.10 | Windows version 10.0 (Windows 10 or greater), amd64 executable | DCO version: N/A | Originally developed by James Yonan | Copyright (C) 2002-2024 OpenVPN Inc <sales@openvpn.net> | Compile time defines: N/A
== wintun ==
  D:\a\222\222\work\wintun\wintun\LICENSE.txt
  D:\a\222\222\work\wintun\wintun\README.md
  D:\a\222\222\work\wintun\wintun\bin\amd64\wintun.dll
  D:\a\222\222\work\wintun\wintun\bin\arm\wintun.dll
  D:\a\222\222\work\wintun\wintun\bin\arm64\wintun.dll
  D:\a\222\222\work\wintun\wintun\bin\x86\wintun.dll
  D:\a\222\222\work\wintun\wintun\include\wintun.h
wintun.dll copied
== tap-windows6 installer ==
tap installer exit=0
adapters after tap install:
  Ethernet 4 | Mellanox ConnectX-4 Lx Virtual Ethernet Adapter | Up
  vEthernet (nat) | Hyper-V Virtual Ethernet Adapter | Up
  Ethernet 3 | Microsoft Hyper-V Network Adapter #3 | Up
  Local Area Connection | TAP-Windows Adapter V9 | Disconnected
== create tap adapter ==
tapctl create exit=0
  TAP: EndorVPN | TAP-Windows Adapter V9 #2 | Disconnected
  TAP: Local Area Connection | TAP-Windows Adapter V9 | Disconnected
== vpngate parse ==
bytes=1347223 in 1840ms
parsed servers=0
countries=0 :: 
chosen:   ping= speed=
config len=0
config: 
ip before: {"status":"success","country":"United States","countryCode":"US","city":"Phoenix","query":"172.182.224.192"}
== firewall test ==
  Ok.
  
  
  Deleted 1 rule(s).
  Ok.
  
