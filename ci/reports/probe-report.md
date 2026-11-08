== system ==
OS: Microsoft Windows Server 2025 Datacenter build 10.0.26100.0
Admin: True
PS: 7.6.9
7z: C:\ProgramData\Chocolatey\bin\7z.exe
FreeDisk: 31.1600227355957 GB
== connectivity ==
OK   200 https://www.vpngate.net/api/iphone/
OK   200 https://swupdate.openvpn.org/community/releases/OpenVPN-2.6.12-I001-amd64.msi
OK   200 https://build.openvpn.net/downloads/releases/tap-windows-9.24.7-I601-Win10.exe
OK   200 https://raw.githubusercontent.com/rainsstory/vpngate-mirror/main/api/iphone/index.html
FAIL https://api.ipify.org?format=json :: Response status code does not indicate success: 520 (<none>).
OK   200 http://ip-api.com/json/?fields=status,country,countryCode,city,query
== openvpn msi ==
downloaded 5.171875 MB
extracted tree:
  ovpn.msi  1780KB
  OpenVPN\license.txt  29.6KB
  OpenVPN\bin\libcrypto-3-x64.dll  4590.3KB
  OpenVPN\bin\libopenvpn_plap.dll  920.8KB
  OpenVPN\bin\libpkcs11-helper-1.dll  110.8KB
  OpenVPN\bin\libssl-3-x64.dll  809.8KB
  OpenVPN\bin\openssl.exe  764.3KB
  OpenVPN\bin\openvpn-gui.exe  868.3KB
  OpenVPN\bin\openvpn-plap-install.reg  0.4KB
  OpenVPN\bin\openvpn-plap-uninstall.reg  0.2KB
  OpenVPN\bin\openvpn.exe  1014.8KB
  OpenVPN\bin\openvpnserv.exe  63.3KB
  OpenVPN\bin\openvpnserv2.exe  23.9KB
  OpenVPN\bin\tapctl.exe  41.3KB
  OpenVPN\bin\vcruntime140.dll  94.9KB
  OpenVPN\config\README.txt  0.3KB
  OpenVPN\config-auto\README.txt  0.6KB
  OpenVPN\doc\INSTALL-win32.txt  2.8KB
  OpenVPN\doc\openvpn.8.html  386.9KB
  OpenVPN\Documentation\OpenVPN HOWTO.url  0.1KB
  OpenVPN\Documentation\OpenVPN Support.url  0.2KB
  OpenVPN\Documentation\OpenVPN Web Site.url  0.1KB
  OpenVPN\Documentation\OpenVPN Wiki.url  0.1KB
  OpenVPN\easy-rsa\ChangeLog  14.9KB
  OpenVPN\easy-rsa\COPYING.html  1.2KB
  OpenVPN\easy-rsa\COPYING.md  1.3KB
  OpenVPN\easy-rsa\easyrsa  141KB
  OpenVPN\easy-rsa\EasyRSA-Start.bat  0.2KB
  OpenVPN\easy-rsa\openssl-easyrsa.cnf  5KB
  OpenVPN\easy-rsa\README-Windows.txt  4.2KB
  OpenVPN\easy-rsa\README.html  2.9KB
  OpenVPN\easy-rsa\README.quickstart.html  3.4KB
  OpenVPN\easy-rsa\vars.example  8.9KB
  OpenVPN\easy-rsa\bin\awk.exe  200KB
  OpenVPN\easy-rsa\bin\cat.exe  16KB
  OpenVPN\easy-rsa\bin\cp.exe  72KB
  OpenVPN\easy-rsa\bin\date.exe  38.5KB
  OpenVPN\easy-rsa\bin\diff.exe  66.5KB
  OpenVPN\easy-rsa\bin\easyrsa-shell-init.sh  4.3KB
  OpenVPN\easy-rsa\bin\grep.exe  132.5KB
  OpenVPN\easy-rsa\bin\ls.exe  56.5KB
  OpenVPN\easy-rsa\bin\md5sum.exe  30KB
  OpenVPN\easy-rsa\bin\mkdir.exe  32KB
  OpenVPN\easy-rsa\bin\mktemp.exe  25KB
  OpenVPN\easy-rsa\bin\mv.exe  68KB
  OpenVPN\easy-rsa\bin\printf.exe  13KB
  OpenVPN\easy-rsa\bin\rm.exe  64KB
  OpenVPN\easy-rsa\bin\sed.exe  92.5KB
  OpenVPN\easy-rsa\bin\sh.exe  287KB
  OpenVPN\easy-rsa\bin\which.exe  12KB
  OpenVPN\easy-rsa\doc\EasyRSA-Advanced.html  9.7KB
  OpenVPN\easy-rsa\doc\EasyRSA-Readme.html  10.2KB
  OpenVPN\easy-rsa\doc\EasyRSA-Upgrade-Notes.html  2.8KB
  OpenVPN\easy-rsa\doc\Hacking.html  6.3KB
  OpenVPN\easy-rsa\doc\Intro-To-PKI.html  4.9KB
  OpenVPN\easy-rsa\Licensing\gpl-2.0.txt  17.7KB
  OpenVPN\easy-rsa\Licensing\LICENSE-OpenSSL.txt  6KB
  OpenVPN\easy-rsa\Licensing\mksh-Win32.txt  6.4KB
  OpenVPN\easy-rsa\Licensing\mktemp.txt  1KB
  OpenVPN\easy-rsa\x509-types\ca  0.4KB
  OpenVPN\easy-rsa\x509-types\client  0.2KB
  OpenVPN\easy-rsa\x509-types\code-signing  0.2KB
  OpenVPN\easy-rsa\x509-types\COMMON  0.5KB
  OpenVPN\easy-rsa\x509-types\email  0.2KB
  OpenVPN\easy-rsa\x509-types\kdc  0.6KB
  OpenVPN\easy-rsa\x509-types\server  0.2KB
  OpenVPN\easy-rsa\x509-types\serverClient  0.2KB
  OpenVPN\include\tap-windows.h  3.2KB
  OpenVPN\log\README.txt  0.2KB
  OpenVPN\res\ovpn.ico  54KB
  OpenVPN\sample-config\client.ovpn  3.5KB
  OpenVPN\sample-config\server.ovpn  10.8KB
  OpenVPN\ssl\modules\legacy.dll  109.3KB
== tap windows driver ==
tap installer 0.577537536621094 MB
== vpngate api ==
bytes=1347423 in 1806ms
lines=104
header=#HostName,IP,Score,Ping,Speed,CountryLong,CountryShort,NumVpnSessions,Uptime,TotalUsers,TotalTraffic,LogType,Operator,Message,OpenVPN_ConfigData_Base64
countries=0
country list=
sample fields: host=public-vpn-108 ip=219.100.37.98 score=3037443 ping=17 speed=365617844 country=Japan cc=JP sessions=130
decoded config len=0
== mirror ==
mirror bytes=1347307
== tap install attempt ==
inf found: 
adapter: Ethernet 4 | Mellanox ConnectX-4 Lx Virtual Ethernet Adapter | Up
adapter: Ethernet 3 | Microsoft Hyper-V Network Adapter #3 | Up
adapter: vEthernet (nat) | Hyper-V Virtual Ethernet Adapter | Up
== real vpn test ==
ip before: {"ip":"135.232.224.160"}
openvpn exe: D:\a\222\222\ovpnx\OpenVPN\bin\openvpn.exe
openvpn pid=2012
ip after: {"ip":"135.232.224.160"}
--- routes ---
  255.255.255.255/32 via 0.0.0.0 if vEthernet (nat) metric 256
  255.255.255.255/32 via 0.0.0.0 if Ethernet 3 metric 256
  255.255.255.255/32 via 0.0.0.0 if Loopback Pseudo-Interface 1 metric 256
  224.0.0.0/4 via 0.0.0.0 if vEthernet (nat) metric 256
  224.0.0.0/4 via 0.0.0.0 if Ethernet 3 metric 256
  224.0.0.0/4 via 0.0.0.0 if Loopback Pseudo-Interface 1 metric 256
  172.30.95.255/32 via 0.0.0.0 if vEthernet (nat) metric 256
  172.30.80.1/32 via 0.0.0.0 if vEthernet (nat) metric 256
  172.30.80.0/20 via 0.0.0.0 if vEthernet (nat) metric 256
  127.255.255.255/32 via 0.0.0.0 if Loopback Pseudo-Interface 1 metric 256
  127.0.0.1/32 via 0.0.0.0 if Loopback Pseudo-Interface 1 metric 256
  127.0.0.0/8 via 0.0.0.0 if Loopback Pseudo-Interface 1 metric 256
  10.1.15.255/32 via 0.0.0.0 if Ethernet 3 metric 256
  10.1.0.100/32 via 0.0.0.0 if Ethernet 3 metric 256
  10.1.0.0/20 via 0.0.0.0 if Ethernet 3 metric 256
--- openvpn log tail ---
  Options error: You must define TUN/TAP device (--dev)
  Use --help for more information.
