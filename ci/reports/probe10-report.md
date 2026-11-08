cred: u=vpnbook p=eetk7pw
== download configs ==
  OK tcp443 us16.vpnbook.com bytes=4421
  OK udp25000 us16.vpnbook.com bytes=4423
  OK tcp80 us16.vpnbook.com bytes=4420
  OK udp53 us16.vpnbook.com bytes=4420
  OK tcp443 de20.vpnbook.com bytes=4421
  OK udp25000 de20.vpnbook.com bytes=4423
  OK tcp80 de20.vpnbook.com bytes=4420
  OK udp53 de20.vpnbook.com bytes=4420
  OK tcp443 fr200.vpnbook.com bytes=4422
  OK udp25000 fr200.vpnbook.com bytes=4424
  OK tcp80 fr200.vpnbook.com bytes=4421
  OK udp53 fr200.vpnbook.com bytes=4421
configs=12
--- sample config ---
   client
   dev tun1
   proto tcp
   remote us16.vpnbook.com 443
   resolv-retry infinite
   nobind
   persist-key
   persist-tun
   auth-user-pass
   verb 3
   cipher AES-256-GCM
   auth SHA256
   data-ciphers AES-256-GCM:AES-128-GCM
   fast-io
   pull
   route-delay 2
   redirect-gateway
   <ca>
   -----BEGIN CERTIFICATE-----
   MIIDSzCCAjOgAwIBAgIUJdJ6+6lTiYZBvpl2P40Lgx3BeHowDQYJKoZIhvcNAQEL
   BQAwFjEUMBIGA1UEAwwLdnBuYm9vay5jb20wHhcNMjMwMjIwMTk0NTM1WhcNMzMw
   MjE3MTk0NTM1WjAWMRQwEgYDVQQDDAt2cG5ib29rLmNvbTCCASIwDQYJKoZIhvcN
   AQEBBQADggEPADCCAQoCggEBAMcVK+hYl6Wl57YxXIVy7Jlgglj42LaC2sUWK3ls
   aRcKQfs/ridG6+9dSP1ziCrZ1f5pOLz34gMYXChhUOc/x9rSIRGHao4gHeXmEoGs
   twjxA+kRBSv5xqeUgaTKAhdwiV5SvBE8EViWe3rlHLoUbWBQ7Kky/L4cg7u+ma1V
== openvpn ==
ip before: {"status":"success","country":"United States","countryCode":"US","city":"Quincy","query":"4.155.237.0"}
  us16.vpnbook.com tcp443 => False (7s)
     2026-11-08 17:25:14 us=234000   connect_timeout = 10
     2026-11-08 17:25:14 us=234000   keepalive_timeout = 0
  us16.vpnbook.com udp25000 => False (3s)
     2026-11-08 17:25:23 us=812000   connect_timeout = 10
     2026-11-08 17:25:23 us=812000   keepalive_timeout = 0
  us16.vpnbook.com tcp80 => False (0s)
     2026-11-08 17:25:28 us=359000   connect_timeout = 10
     2026-11-08 17:25:28 us=359000   keepalive_timeout = 0
  us16.vpnbook.com udp53 => False (0s)
     2026-11-08 17:25:29 us=875000   connect_timeout = 10
     2026-11-08 17:25:29 us=875000   keepalive_timeout = 0
  de20.vpnbook.com tcp443 => False (4s)
     2026-11-08 17:25:31 us=390000   connect_timeout = 10
     2026-11-08 17:25:31 us=390000   keepalive_timeout = 0
  de20.vpnbook.com udp25000 => False (6s)
     2026-11-08 17:25:36 us=906000   connect_timeout = 10
     2026-11-08 17:25:36 us=906000   keepalive_timeout = 0
  de20.vpnbook.com tcp80 => False (3s)
     2026-11-08 17:25:44 us=421000   connect_timeout = 10
     2026-11-08 17:25:44 us=421000   keepalive_timeout = 0
  de20.vpnbook.com udp53 => False (2s)
     2026-11-08 17:25:48 us=953000   connect_timeout = 10
     2026-11-08 17:25:48 us=953000   keepalive_timeout = 0
  fr200.vpnbook.com tcp443 => False (1s)
     2026-11-08 17:25:52 us=468000   connect_timeout = 10
     2026-11-08 17:25:52 us=484000   keepalive_timeout = 0
  fr200.vpnbook.com udp25000 => False (0s)
     2026-11-08 17:25:54 us=984000   connect_timeout = 10
     2026-11-08 17:25:54 us=984000   keepalive_timeout = 0
  fr200.vpnbook.com tcp80 => False (1s)
     2026-11-08 17:25:56 us=515000   connect_timeout = 10
     2026-11-08 17:25:56 us=515000   keepalive_timeout = 0
  fr200.vpnbook.com udp53 => False (5s)
     2026-11-08 17:25:58 us=15000   connect_timeout = 10
     2026-11-08 17:25:58 us=15000   keepalive_timeout = 0
NO WORKING CONFIG
