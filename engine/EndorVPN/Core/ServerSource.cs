using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EndorVPN.Core
{
    public sealed class VpnServer
    {
        public string Host { get; set; }
        public string Ip { get; set; }
        public long Score { get; set; }
        public int Ping { get; set; }
        public long SpeedBps { get; set; }
        public string Country { get; set; }
        public string CountryCode { get; set; }
        public int Sessions { get; set; }
        public string Operator { get; set; }
        public string ConfigBase64 { get; set; }

        /// <summary>"vpngate" or "vpnbook". Drives which auth method the engine uses.</summary>
        public string Provider { get; set; } = "vpngate";
        public string AuthUser { get; set; }
        public string AuthPass { get; set; }

        /// <summary>Last measured TCP connect time (ms), 0 when unmeasured. Not persisted.</summary>
        [System.Xml.Serialization.XmlIgnore]
        public int _measuredPing { get; set; }

        /// <summary>
        /// Identity for dedupe and transport bookkeeping. The protocol is part of it:
        /// VPNBook serves the same host over tcp443 and udp25000, and without this
        /// the two profiles collided, so the UDP one silently replaced the TCP one
        /// (or the other way round) and only TCP was ever dialled.
        /// </summary>
        public string Id => (CountryCode ?? "??") + "|" + (Host ?? "") + "|" + (Ip ?? "") + "|" + ProtoTag;

        /// <summary>"udp"/"tcp" from the profile, empty when unknown.</summary>
        public string ProtoTag
        {
            get
            {
                try
                {
                    var cfg = ServerSource.DecodeConfig(this);
                    if (string.IsNullOrEmpty(cfg)) return "";
                    if (cfg.IndexOf("proto udp", StringComparison.OrdinalIgnoreCase) >= 0) return "udp";
                    if (cfg.IndexOf("proto tcp", StringComparison.OrdinalIgnoreCase) >= 0) return "tcp";
                }
                catch { }
                return "";
            }
        }

        /// <summary>Country name fallback for locales without a translation.</summary>
        public string CountryName => string.IsNullOrWhiteSpace(Country) ? (CountryCode ?? "?") : Country;
    }

    /// <summary>
    /// Pulls the public VPN Gate aggregate list.
    ///
    /// VPN Gate publishes a CSV where the last column is a base64 OpenVPN profile.
    /// The endpoint is reachable over both HTTPS (vpnbook-style networks) and HTTP;
    /// we also keep a GitHub mirror of the same aggregate list as a third source so
    /// the picker still fills in when the main site is unreachable from the user's
    /// country.
    /// </summary>
    public static class ServerSource
    {
        public const string PrimaryUrl = "https://www.vpngate.net/api/iphone/";
        public const string FallbackUrl = "http://www.vpngate.net/api/iphone/";
        public const string MirrorUrl = "https://raw.githubusercontent.com/rainsstory/vpngate-mirror/main/api/iphone/index.html";

        public static List<VpnServer> Parse(string raw, Log log)
        {
            var list = new List<VpnServer>(256);
            if (string.IsNullOrEmpty(raw)) return list;

            // Some hosts wrap the CSV in an HTML document; extract the payload.
            int start = raw.IndexOf("*vpn_servers", StringComparison.Ordinal);
            if (start > 0) raw = raw.Substring(start);
            int end = raw.LastIndexOf("\n*", StringComparison.Ordinal);
            if (end > start && end > 0) raw = raw.Substring(0, end);

            foreach (var rawLine in raw.Split('\n'))
            {
                string line = rawLine.Trim('\r', ' ', '\t');
                if (line.Length == 0 || line[0] == '*' || line[0] == '#') continue;
                // A config blob itself is not CSV: require the generic shape first.
                if (line.IndexOf(',') < 0) continue;
                string[] f = line.Split(',');
                if (f.Length < 15) continue;
                string b64 = f[f.Length - 1];
                if (b64.Length < 200) continue;

                var s = new VpnServer
                {
                    Host = f[0].Trim(),
                    Ip = f[1].Trim(),
                    Score = ParseLong(f[2]),
                    Ping = (int)ParseLong(f[3]),
                    SpeedBps = ParseLong(f[4]),
                    Country = f[5].Trim(),
                    CountryCode = f[6].Trim().ToUpperInvariant(),
                    Sessions = (int)ParseLong(f[7]),
                    Operator = f[12].Trim(),
                    ConfigBase64 = b64.Trim(),
                };
                // Keep nodes whose country we cannot map: dropping them silently
                // shrank the list for no reason. They show up under an "unknown"
                // grouping instead.
                if (string.IsNullOrEmpty(s.CountryCode)) s.CountryCode = "??";
                list.Add(s);
            }
            log.Info("parsed " + list.Count + " servers");
            return list;
        }

        private static long ParseLong(string s)
        {
            long v;
            return long.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : 0L;
        }

        public static async Task<string> FetchAsync(string url, int timeoutMs, CancellationToken ct)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            using (var client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "EndorVPN/1.0 (+https://github.com)");
                return await client.GetStringAsync(url).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Fetches from every source in parallel and uses the first non-empty
        /// answer, so a blocked primary host costs nothing in perceived speed.
        /// </summary>
        public static async Task<List<VpnServer>> LoadAsync(Log log, CancellationToken ct)
        {
            var urls = new[] { PrimaryUrl, FallbackUrl, MirrorUrl };
            var tasks = new List<Task<List<VpnServer>>>();
            foreach (var url in urls)
            {
                var captured = url;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        string raw = await FetchAsync(captured, 14000, ct).ConfigureAwait(false);
                        sw.Stop();
                        log.Info("server list from " + captured + " in " + sw.ElapsedMilliseconds + " ms, " + raw.Length + " chars");
                        return Parse(raw, log);
                    }
                    catch (OperationCanceledException) { return new List<VpnServer>(); }
                    catch (Exception ex)
                    {
                        log.Warn("source failed " + captured + ": " + ex.Message);
                        return new List<VpnServer>();
                    }
                }, ct));
            }
            // Merge every source instead of taking the first one that answers.
            //
            // The mirror and vpngate.net publish different snapshots of the
            // volunteer pool: whichever replied first used to win outright, so the
            // country list depended on network timing and often showed only the
            // 12 countries present in the fastest mirror. Union them by Id.
            var merged = new List<VpnServer>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (tasks.Count > 0)
            {
                var finished = await Task.WhenAny(tasks).ConfigureAwait(false);
                tasks.Remove(finished);
                var list = finished.Result;
                if (list == null) continue;
                foreach (var s in list)
                    if (seen.Add(s.Id)) merged.Add(s);
            }
            log.Info("merged server list: " + merged.Count + " servers from " + urls.Length + " sources");
            if (merged.Count == 0) log.Error("all server sources failed");
            return merged;
        }

        public static string DecodeConfig(VpnServer server)
        {
            if (server == null || string.IsNullOrEmpty(server.ConfigBase64)) return null;
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(server.ConfigBase64));
            }
            catch
            {
                return null;
            }
        }
    }
}
