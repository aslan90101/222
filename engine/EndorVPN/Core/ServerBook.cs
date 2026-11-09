using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EndorVPN.Core
{
    /// <summary>
    /// Holds the server list, groups it by country and ranks it.
    ///
    /// Ranking used by the picker and by "smart pick":
    ///   * latency measured with a real TCP handshake against the control port
    ///   * the feed's own volunteered speed (bps) and user count
    ///   * penalize badly crowded nodes (many sessions on a volunteer box)
    /// </summary>
    public sealed class ServerBook
    {
        public sealed class Country
        {
            public string Code;
            public string Name;
            public List<VpnServer> Servers = new List<VpnServer>();
            public int BestPing = int.MaxValue;
            public long BestSpeed = 0;
            public int PingSamples;
            public bool IsFavorite;

            public VpnServer Best()
            {
                // Same reasoning as SmartPick: a reachable VPNBook node beats a
                // theoretically faster VPN Gate volunteer that answers AUTH_FAILED.
                return Servers
                    .OrderBy(s => string.Equals(s.Provider, "vpnbook", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(s => s._measuredPing > 0 ? s._measuredPing : (s.Ping > 0 ? s.Ping : 9999))
                    .ThenByDescending(s => s.SpeedBps)
                    .FirstOrDefault();
            }
        }

        private readonly Log _log;
        public List<VpnServer> Servers { get; private set; } = new List<VpnServer>();
        public Dictionary<string, Country> Countries { get; private set; } = new Dictionary<string, Country>(StringComparer.OrdinalIgnoreCase);
        public DateTime LastUpdate { get; private set; } = DateTime.MinValue;

        public ServerBook(Log log)
        {
            _log = log;
        }

        public void SetServers(IEnumerable<VpnServer> servers, IEnumerable<string> favorites)
        {
            Servers = servers.ToList();
            Countries = new Dictionary<string, Country>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in Servers)
            {
                Country c;
                if (!Countries.TryGetValue(s.CountryCode, out c))
                {
                    c = new Country { Code = s.CountryCode, Name = s.CountryName };
                    Countries[s.CountryCode] = c;
                }
                c.Servers.Add(s);
            }
            if (favorites != null)
            {
                foreach (var f in favorites)
                {
                    Country c;
                    if (Countries.TryGetValue(f, out c)) c.IsFavorite = true;
                }
            }
            foreach (var c in Countries.Values)
            {
                c.BestPing = c.Servers.Where(x => x.Ping > 0).Select(x => x.Ping).DefaultIfEmpty(int.MaxValue).Min();
                c.BestSpeed = c.Servers.Select(x => x.SpeedBps).DefaultIfEmpty(0).Max();
            }
            LastUpdate = DateTime.Now;
            _log.Info("server book: " + Servers.Count + " servers in " + Countries.Count + " countries");
        }

        /// <summary>
        /// Measures TCP connect time to each country's best few servers. Runs with
        /// bounded parallelism so a 300-server feed does not take minutes.
        /// </summary>
        public async Task MeasureLatencyAsync(IProgress<int> progress, CancellationToken ct)
        {
            var sample = new List<VpnServer>();
            foreach (var c in Countries.Values)
            {
                foreach (var s in c.Servers
                    .OrderByDescending(x => x.SpeedBps)
                    .ThenBy(x => x.Ping)
                    .Take(3))
                {
                    sample.Add(s);
                }
            }
            int done = 0;
            int total = Math.Max(1, sample.Count);
            var gate = new SemaphoreSlim(12);
            var tasks = new List<Task>();
            foreach (var s in sample)
            {
                if (ct.IsCancellationRequested) break;
                await gate.WaitAsync(ct).ConfigureAwait(false);
                var server = s;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        int port = 443;
                        int ping = IpChecker.TcpPing(server.Ip, port, 1800);
                        if (ping < 0) ping = IpChecker.TcpPing(server.Ip, 80, 1800);
                        server._measuredPing = ping;
                    }
                    catch { }
                    finally
                    {
                        gate.Release();
                        int d = Interlocked.Increment(ref done);
                        if (progress != null && d % 8 == 0) progress.Report(100 * d / total);
                    }
                }, ct));
            }
            try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }

            foreach (var c in Countries.Values)
            {
                var pings = c.Servers.Where(x => x._measuredPing > 0).Select(x => x._measuredPing).ToList();
                c.PingSamples = pings.Count;
                c.BestPing = pings.Count > 0 ? pings.Min() : c.BestPing;
            }
            _log.Info("latency measured for " + done + " servers");
        }

        public Country GetCountry(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            Country c;
            return Countries.TryGetValue(code, out c) ? c : null;
        }

        /// <summary>Best overall server, used by smart pick and auto-connect.</summary>
        public VpnServer SmartPick()
        {
            // VPN Gate volunteers answer AUTH_FAILED for many client networks,
            // VPNBook's pool does not. Reliable beats theoretically faster.
            return Servers
                .OrderBy(s => string.Equals(s.Provider, "vpnbook", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(s => s._measuredPing > 0 ? s._measuredPing : (s.Ping > 0 ? s.Ping : 9000))
                .ThenByDescending(s => s.SpeedBps)
                .ThenBy(s => s.Sessions)
                .FirstOrDefault();
        }

        /// <summary>
        /// Best server from a different provider than the one that just failed,
        /// preferring the same country and then a geographically close one.
        /// VPN Gate rejects anonymous logins from many networks; VPNBook keeps a
        /// small, stable pool that is our reliable fallback.
        /// </summary>
        public VpnServer FallbackFor(VpnServer failed, string countryCode)
        {
            if (failed == null) return null;
            var pool = Servers.Where(s => s.Provider != failed.Provider && !string.IsNullOrEmpty(s.Host)).ToList();
            if (pool.Count == 0) return null;

            var same = pool.Where(s => string.Equals(s.CountryCode, failed.CountryCode, StringComparison.OrdinalIgnoreCase)).ToList();
            if (same.Count > 0) return Best(same);

            var wanted = string.Equals(countryCode, failed.CountryCode, StringComparison.OrdinalIgnoreCase) ? countryCode : null;
            if (wanted != null)
            {
                var byCountry = pool.Where(s => string.Equals(s.CountryCode, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
                if (byCountry.Count > 0) return Best(byCountry);
            }
            return Best(pool);
        }

        private static VpnServer Best(List<VpnServer> list)
        {
            return list
                .OrderBy(s => s._measuredPing > 0 ? s._measuredPing : (s.Ping > 0 ? s.Ping : 9000))
                .ThenByDescending(s => s.SpeedBps)
                .FirstOrDefault();
        }

        /// <summary>Ranked list of countries for the picker, honouring favorites.</summary>
        public List<Country> Ranking(string filter, string query, out List<Country> favoritesList)
        {
            IEnumerable<Country> all = Countries.Values;
            favoritesList = all.Where(c => c.IsFavorite).ToList();
            switch (filter)
            {
                case "favorite":
                    all = favoritesList;
                    break;
                case "fast":
                    // No arbitrary cut-off: "fast" should mean "sorted by latency",
                    // not "only the first 15 countries exist".
                    all = all.OrderBy(c => c.BestPing <= 0 ? int.MaxValue : c.BestPing);
                    break;
            }
            if (!string.IsNullOrWhiteSpace(query))
            {
                string q = query.Trim().ToLowerInvariant();
                all = all.Where(c =>
                    (c.Name ?? "").ToLowerInvariant().Contains(q) ||
                    c.Code.ToLowerInvariant().Contains(q) ||
                    Loc.Country(c.Code, c.Name).ToLowerInvariant().Contains(q) ||
                    c.Servers.Any(s => (s.Host ?? "").ToLowerInvariant().Contains(q)));
            }
            return all
                .OrderByDescending(c => c.IsFavorite)
                .ThenBy(c => c.BestPing <= 0 ? 900000 : c.BestPing)
                .ThenByDescending(c => c.BestSpeed)
                .ToList();
        }
    }


}
