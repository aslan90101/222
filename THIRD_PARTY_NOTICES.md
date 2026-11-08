# Third-party notices

EndorVPN itself is MIT licensed. The portable build bundles the following
components; their licenses apply to the files they ship with:

| Component | Version | License | Files |
|---|---|---|---|
| OpenVPN | 2.6.12 | GPLv2 | `engine/openvpn.exe`, `engine/openssl.exe`, `engine/tapctl.exe`, `engine/libcrypto-3-x64.dll`, `engine/libssl-3-x64.dll`, `engine/vcruntime140.dll` |
| TAP-Windows6 driver | 9.24.7 | GPLv2 | `engine/drivers/tap-windows-installer.exe` |
| flag-icons (flags) | master | MIT | embedded PNG flags generated from https://github.com/lipis/flag-icons |
| CLDR locale data | 45 | Unicode-DFS-2016 | embedded country names (generated at build time) |

Source code for the GPL components is available from the upstream projects
(openvpn.net, github.com/OpenVPN/tap-windows6). EndorVPN only invokes the
OpenVPN binary as a separate process and does not link against it.

Servers are provided by the volunteer VPN Gate project (University of Tsukuba)
and VPNBook. EndorVPN does not operate any VPN servers.
