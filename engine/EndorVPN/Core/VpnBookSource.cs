using System;
using System.Collections.Generic;
using System.Globalization;
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

                // only touch hosts that are actually listed on the page
                var hosts = new List<string>();
                foreach (var kv in HostToCountry)
                {
                    if (page.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) hosts.Add(kv.Key);
                }
                if (hosts.Count == 0) hosts.AddRange(HostToCountry.Keys);

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
                                var srv = new VpnServer
                                {
                                    Host = h,
                                    Ip = h,
                                    CountryCode = HostToCountry[h],
                                    Country = CountryNames[HostToCountry[h]],
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
