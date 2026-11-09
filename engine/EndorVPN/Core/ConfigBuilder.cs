using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Linq;
using System.Text;

namespace EndorVPN.Core
{
    /// <summary>
    /// Turns a VPN Gate / VPNBook profile into the config we actually run.
    ///
    /// Rules learned from real servers:
    ///   * VPN Gate profiles list a random control port that is often blocked;
    ///     we rewrite it to a port that almost always passes (TCP 443 first).
    ///   * TAP adapter name is forced to ours so we never grab the user's
    ///     unrelated "Local Area Connection" adapter.
    ///   * DNS is pinned to the servers' pushed DNS with a public fallback.
    ///   * Windows needs a deterministic driver: TAP-Windows6 (works everywhere
    ///     incl. Windows 7), Wintun is used only when explicitly allowed.
    /// </summary>
    public static class ConfigBuilder
    {
        public sealed class Options
        {
            public string AdapterName = "EndorVPN";
            public Transport Transport = Transport.Auto;
            public string AuthFile;
            public bool UseWintun;
            public int ConnectTimeoutSec = 25;
            public int TunMtu = 1450;

            /// <summary>Override the profile's own port (transport probing).</summary>
            public int RemotePort;

            /// <summary>Suppress block-outside-dns (it can starve the TAP DHCP lease).</summary>
            public bool NoDnsBlock;
        }

        public enum Transport { Auto, Tcp, Udp }

        public static string Build(VpnServer server, string rawConfig, Options opts)
        {
            if (string.IsNullOrEmpty(rawConfig)) throw new ArgumentException("empty config");
            opts = opts ?? new Options();

            var lines = rawConfig.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
            var head = new List<string>();
            var blocks = new List<string>();      // <ca>, <cert>, <key>, ...
            bool inBlock = false;
            var current = new StringBuilder();

            foreach (var l in lines)
            {
                var t = l.Trim();
                if (inBlock)
                {
                    current.AppendLine(l);
                    if (t.StartsWith("</")) { inBlock = false; blocks.Add(current.ToString()); current.Clear(); }
                    continue;
                }
                if (t.StartsWith("<") && t.IndexOf('>') > 1 && !t.StartsWith("</"))
                {
                    inBlock = true;
                    current.AppendLine(l);
                    continue;
                }
                if (t.Length == 0) continue;
                if (t.StartsWith("#") || t.StartsWith(";")) continue;
                head.Add(t);
            }

            string remoteHost;
            int remotePort;
            bool remoteIsDomain;
            string proto;
            ReadRemote(head, out remoteHost, out remotePort, out remoteIsDomain, out proto);

            bool tcp = opts.Transport == Transport.Tcp || (opts.Transport == Transport.Auto && proto == "tcp");

            var outLines = new List<string>();
            outLines.Add("# EndorVPN profile for " + server.Host + " (" + server.CountryCode + ")");
            outLines.Add("client");
            outLines.Add("dev tun");
            outLines.Add("nobind");
            outLines.Add("persist-key");
            outLines.Add("persist-tun");
            outLines.Add("resolv-retry infinite");
            outLines.Add("verb 3");
            outLines.Add("remote-cert-tls server");
            outLines.Add("auth-nocache");
            outLines.Add("route-delay 1");
            // TAP-Windows6 emulates a DHCP server and leaves the interface down
            // until Windows accepts the lease. When the DHCP Client service is
            // disabled/slow the adapter never comes up and OpenVPN spins forever on
            // "Route: Waiting for TUN/TAP interface to come up". netsh sets the
            // address directly, no lease involved.
            outLines.Add("ip-win32 netsh");
            outLines.Add("connect-retry-max 1");
            outLines.Add("connect-timeout " + Math.Max(8, opts.ConnectTimeoutSec));
            outLines.Add("tun-mtu " + opts.TunMtu);
            outLines.Add("mssfix 1380");
            outLines.Add("tls-version-min 1.2");
            outLines.Add("windows-driver " + (opts.UseWintun ? "wintun" : "tap-windows6"));
            if (!opts.UseWintun)
            {
                outLines.Add("dev-node \"" + (opts.AdapterName ?? "EndorVPN") + "\"");
            }
            if (tcp) outLines.Add("proto tcp");
            else outLines.Add("proto udp");
            if (opts.RemotePort > 0)
            {
                outLines.Add("remote " + remoteHost + " " + opts.RemotePort);
            }
            else
            {
                outLines.Add("remote " + remoteHost + " 443");
                if (remoteIsDomain)
                {
                    // remote-random shuffles the remote *list*, and with two ports
                    // listed it was silently picking 1194 half the time - a port the
                    // profile never advertised. Keep the profile's own port only.
                    outLines.Add("remote " + remoteHost + " " + remotePort);
                }
            }

            if (!string.IsNullOrEmpty(opts.AuthFile))
            {
                outLines.Add("auth-user-pass \"" + opts.AuthFile + "\"");
            }

            // Carry over the server's crypto policy verbatim: VPN Gate nodes differ
            // a lot (CBC + SHA1 on old nodes, GCM on modern ones).
            foreach (var key in new[] { "cipher", "data-ciphers", "data-ciphers-fallback", "auth", "tls-cipher", "key-direction", "remote-random-hostname", "comp-lzo", "compress" })
            {
                var match = head.FirstOrDefault(l => l.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase));
                if (match != null) outLines.Add(match);
            }

            // Server-specific directives we must keep as-is.
            foreach (var l in head)
            {
                if (l.StartsWith("route ", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("route-ipv6 ", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("pull-filter ", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("tls-crypt ", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("tls-auth ", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("ca ", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("cert ", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("key ", StringComparison.OrdinalIgnoreCase))
                {
                    outLines.Add(l);
                }
            }

            // A public DNS pair on top of whatever the server pushes. If the server
            // pushes block-outside-dns, DNS resets to the adapter's resolver anyway.
            outLines.Add("dhcp-option DNS 1.1.1.1");
            outLines.Add("dhcp-option DNS 8.8.8.8");
            // block-outside-dns installs WFP filters for the whole session. On some
            // Windows builds those filters also swallow the DHCP lease renewal that
            // brings the TAP interface up, which is the one thing we cannot afford.
            // The pushed DNS servers above already win the metric, so drop the block.
            if (!opts.NoDnsBlock) outLines.Add("block-outside-dns");
            outLines.Add("redirect-gateway def1 bypass-dhcp");

            var sb = new StringBuilder();
            foreach (var l in outLines) sb.AppendLine(l);
            foreach (var b in blocks) sb.AppendLine(b.TrimEnd());
            return sb.ToString();
        }

        private static void ReadRemote(List<string> head, out string host, out int port, out bool isDomain, out string proto)
        {
            host = null; port = 443; isDomain = true; proto = "udp";
            foreach (var l in head)
            {
                if (l.StartsWith("proto ", StringComparison.OrdinalIgnoreCase))
                {
                    var p = l.Substring(6).Trim().Split(' ')[0].ToLowerInvariant();
                    if (p == "tcp" || p == "tcp4" || p == "tcp6" || p == "tcp-client") proto = "tcp";
                    else proto = "udp";
                }
                else if ((l.StartsWith("remote ", StringComparison.OrdinalIgnoreCase)) && host == null)
                {
                    string[] f = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length >= 2) host = f[1];
                    int pn;
                    if (f.Length >= 3 && int.TryParse(f[2], NumberStyles.Any, CultureInfo.InvariantCulture, out pn)) port = pn;
                }
            }
            if (string.IsNullOrEmpty(host)) throw new InvalidOperationException("profile has no remote host");
            IPAddress ip;
            isDomain = !IPAddress.TryParse(host, out ip);
        }

        private static bool IsIp(string host)
        {
            System.Net.IPAddress ip;
            return System.Net.IPAddress.TryParse(host, out ip);
        }
    }
}
