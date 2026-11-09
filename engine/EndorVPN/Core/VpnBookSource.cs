using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EndorVPN.Core
{
    /// <summary>
    /// VPNBook provider: a long-running free service with fixed exit nodes in a
    /// handful of countries (US, CA, UK, DE, FR). Its profiles need a shared
    /// username/password that the site rotates every one or two weeks, so we read
    /// the current pair from the same page we scrape the node list from.
    ///
    /// Value for the user: these nodes accept datacenter and residential clients
    /// alike, so they make a reliable fallback when a VPN Gate volunteer node is
    /// overloaded or has stopped accepting new sessions.
    /// </summary>
    public static class VpnBookSource
    {
        public const string PageUrl = "https://www.vpnbook.com/freevpn/openvpn";
        private const string ConfigUrl = "https://www.vpnbook.com/api/openvpn?hostname={0}&protocol={1}";

        private static readonly Dictionary<string, string> HostToCountry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "us16.vpnbook.com", "US" }, { "us178.vpnbook.com", "US" },
            { "ca149.vpnbook.com", "CA" }, { "ca196.vpnbook.com", "CA" },
            { "uk205.vpnbook.com", "GB" }, { "uk68.vpnbook.com", "GB" },
            { "de20.vpnbook.com", "DE" }, { "de220.vpnbook.com", "DE" },
            { "fr200.vpnbook.com", "FR" }, { "fr231.vpnbook.com", "FR" },
        };

        private static readonly Dictionary<string, string> CountryNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "US", "United States" }, { "CA", "Canada" }, { "GB", "United Kingdom" },
            { "DE", "Germany" }, { "FR", "France" },
        };

        public sealed class Bundle
        {
            public List<VpnServer> Servers = new List<VpnServer>();
            public string User;
            public string Password;
            public bool Ok { get { return Servers.Count > 0; } }
        }

        public static async Task<Bundle> LoadAsync(Log log, CancellationToken ct)
        {
            var bundle = new Bundle();
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) })
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) EndorVPN/1.0");

                string page;
                try
                {
                    page = await client.GetStringAsync(PageUrl).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log.Warn("vpnbook page failed: " + ex.Message);
                    return bundle;
                }

                bundle.User = MatchCredentials(page, "Username") ?? "vpnbook";
                bundle.Password = MatchCredentials(page, "Password");
                if (string.IsNullOrEmpty(bundle.Password))
                {
                    log.Warn("vpnbook password not found in page");
                    return bundle;
                }
                log.Info("vpnbook credentials parsed");

                // Discover the hosts from the page itself, not from a fixed table.
                //
                // The old build only looked for ten hard-coded names, so a country
                // VPNBook had just added was invisible until someone shipped a new
                // release. The page lists every live gateway as <host>.vpnbook.com
                // (us16, de220, sg1, jp2 ...), so read them all and map the country
                // from the subdomain prefix; unknowns fall back to the table.
                var hosts = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(page, @"\b([a-z]{2}\d{1,4})\.vpnbook\.com\b",
                             System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    string host = m.Groups[1].Value.ToLowerInvariant() + ".vpnbook.com";
                    if (seen.Add(host)) hosts.Add(host);
                }
                if (hosts.Count == 0)
                {
                    // page layout changed - keep the known set as a safety net
                    foreach (var kv in HostToCountry)
                        if (page.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) hosts.Add(kv.Key);
                }
                if (hosts.Count == 0) hosts.AddRange(HostToCountry.Keys);
                log.Info("vpnbook hosts found: " + hosts.Count + " (" + string.Join(", ", hosts.Take(8)) +
                         (hosts.Count > 8 ? ", ..." : "") + ")");

                var gate = new SemaphoreSlim(4);
                var tasks = new List<Task>();
                var sync = new object();

                foreach (var host in hosts)
                {
                    foreach (var proto in new[] { "tcp443", "udp25000" })
                    {
                        if (ct.IsCancellationRequested) break;
                        await gate.WaitAsync(ct).ConfigureAwait(false);
                        var h = host; var p = proto;
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                string cfg = await client.GetStringAsync(string.Format(ConfigUrl, h, p)).ConfigureAwait(false);
                                if (cfg == null || cfg.Length < 300 || cfg.IndexOf("<ca>", StringComparison.OrdinalIgnoreCase) < 0)
                                    return;
                                string cc = CountryOf(h);
                                var srv = new VpnServer
                                {
                                    Host = h,
                                    Ip = h,
                                    CountryCode = cc,
                                    Country = CountryNames.ContainsKey(cc) ? CountryNames[cc] : cc,
                                    ConfigBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(cfg)),
                                    Provider = "vpnbook",
                                    AuthUser = bundle.User,
                                    AuthPass = bundle.Password,
                                    Operator = "VPNBook",
                                    SpeedBps = 0,
                                    Ping = 0,
                                    Sessions = 0,
                                    Score = 0,
                                };
                                lock (sync) bundle.Servers.Add(srv);
                            }
                            catch (Exception ex)
                            {
                                log.Warn("vpnbook config failed " + h + " " + p + ": " + ex.Message);
                            }
                            finally
                            {
                                gate.Release();
                            }
                        }, ct));
                    }
                }
                try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }
                log.Info("vpnbook servers: " + bundle.Servers.Count);
            }
            return bundle;
        }

        /// <summary>
        /// Country for a gateway from its subdomain prefix (us16 -> US, de220 -> DE,
        /// jp2 -> JP). Unknown two-letter prefixes are still usable, so we return
        /// them uppercased rather than dropping the server.
        /// </summary>
        private static string CountryOf(string host)
        {
            string known;
            if (HostToCountry.TryGetValue(host, out known) && !string.IsNullOrEmpty(known)) return known;

            var m = System.Text.RegularExpressions.Regex.Match(host ?? "", @"^([a-z]{2})\d");
            if (m.Success)
            {
                string cc = m.Groups[1].Value.ToUpperInvariant();
                // ISO 3166 uses GB for the United Kingdom; the subdomain says "uk".
                // Flags and translations are keyed by the ISO code, so map it here.
                if (cc == "UK") cc = "GB";
                return cc;
            }
            return "??";
        }

        /// <summary>Exposed for the smoke test: country mapping must not need a table entry.</summary>
        internal static string CountryForTest(string host) { return CountryOf(host); }

        private static string MatchCredentials(string page, string label)
        {
            if (string.IsNullOrEmpty(page)) return null;
            // The page renders <span>Username</span> ... <code>vpnbook</code> pairs,
            // and the FAQ repeats the same values in plain text. Try both shapes.
            var a = Regex.Match(page, label + @"[^<]{0,120}?<[^>]+>\s*<code[^>]*>([^<]{3,40})</code>", RegexOptions.IgnoreCase);
            if (a.Success) return a.Groups[1].Value.Trim();
            var b = Regex.Match(page, label + @"\s*[:=]\s*([A-Za-z0-9!@#\$%\^&*_\-\+]{3,40})", RegexOptions.IgnoreCase);
            if (b.Success) return b.Groups[1].Value.Trim();
            var c = Regex.Match(page, label + @"[^`]{0,80}?`([^`]{3,40})`", RegexOptions.IgnoreCase);
            if (c.Success) return c.Groups[1].Value.Trim();
            return null;
        }
    }
}
