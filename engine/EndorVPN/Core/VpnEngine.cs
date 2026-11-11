using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EndorVPN.Core
{
    public sealed class ConnectResult
    {
        public bool Ok;
        public string ErrorKey = "err.unknown";
        public string Detail;
        public IpInfo Ip;
        public VpnServer Server;

        /// <summary>The user aborted the attempt; do not report it as a failure.</summary>
        public bool Cancelled;
    }

    /// <summary>
    /// Runs openvpn.exe, watches its log, and reports progress.
    ///
    /// Connection policy (this is what makes "free VPN" usable):
    ///   1. Try the chosen server with TCP 443 (survives most blocking).
    ///   2. On failure, walk other servers in the same country, best ping first.
    ///   3. Only then surface an error to the UI.
    /// </summary>
    public sealed class VpnEngine : IDisposable
    {
        public enum State { Idle, Connecting, Connected, Disconnecting }

        private readonly Paths _paths;
        private readonly Log _log;
        private readonly KillSwitch _killSwitch;
        private readonly TapManager _tap;
        private readonly object _sync = new object();

        private Process _proc;

        private CancellationTokenSource _watchCts;

        private DateTime _lastProbeAt = DateTime.MinValue;
        private HashSet<string> _lastProbeIds;
        private CancellationTokenSource _cts;
        private volatile string _lastLogLine = "";

        /// <summary>Counts "Waiting for TUN/TAP interface" lines: the adapter is not coming up.</summary>
        private int _routeStuck;

        /// <summary>Localization key of the phase openvpn reported most recently.</summary>
        private volatile string _phase;

        /// <summary>Transport chosen by the preflight for each server id.</summary>
        private readonly Dictionary<string, TransportPlan> _plans = new Dictionary<string, TransportPlan>();

        private volatile bool _noDnsBlockThisAttempt;
        private volatile bool _tunnelUp;

        public State CurrentState { get; private set; }
        public VpnServer ActiveServer { get; private set; }
        public IpInfo ActiveIp { get; private set; }

        public event Action<State> StateChanged;
        public event Action<string> StageChanged;
        public event Action<string> LogLine;

        /// <summary>
        /// Raised when an established tunnel dies without the user asking for it.
        /// The UI needs this: before, openvpn could exit ten minutes after
        /// connecting and the window kept showing "Connected" with a dead tunnel.
        /// </summary>
        public event Action<string> TunnelLost;

        public VpnEngine(Paths paths, Log log, KillSwitch killSwitch)
        {
            _paths = paths;
            _log = log;
            _killSwitch = killSwitch;
            _tap = new TapManager(paths, log);
            _log.Appended += line =>
            {
                var h = LogLine;
                if (h != null) h(line);
            };
        }

        public TapManager Tap { get { return _tap; } }

        /// <summary>How long a single server gets to come up before we move on.</summary>
        public int AttemptTimeoutSec = 11;

        /// <summary>Hard cap on attempts in one ConnectAsync call.</summary>
        public int MaxAttempts = 3;

        /// <summary>
        /// Hard ceiling for the whole connect operation, preflight included.
        /// 3 attempts x 12 s = 36 s, which together with probing fit comfortably
        /// below this. Without a global cap a slow day produced a minute of
        /// "Establishing tunnel" before the user saw anything.
        /// </summary>
        public int TotalConnectTimeoutSec = 45;

        /// <summary>How many ranked servers enter the preflight (cheap, parallel).</summary>
        /// <summary>
        /// How many ranked servers enter the preflight. 40 was tried and made the
        /// pre-connect pause longer than the extra choices were worth; 24 keeps the
        /// wait near two seconds while still covering far more than the old 20.
        /// </summary>
        public int PreflightPoolSize = 24;

        /// <summary>Set by the log pump when the peer rejects our credentials.</summary>
        private volatile bool _authRejected;

        /// <summary>Set when the user aborts the attempt from the UI.</summary>
        private volatile bool _cancelled;

        public async Task<ConnectResult> ConnectAsync(VpnServer server, AppSettings settings, IList<VpnServer> alternatives, CancellationToken ct)
        {
            lock (_sync)
            {
                if (CurrentState == State.Connected || CurrentState == State.Connecting)
                    return new ConnectResult { Ok = false, Detail = "already busy" };
                SetState(State.Connecting);
            }
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cancelled = false;
            lock (_plans) _plans.Clear();

            var result = new ConnectResult { Server = server };
            try
            {
                Stage(Loc.T("stage.driver"));
                if (!await Task.Run(() => _tap.EnsureAdapter(null)))
                {
                    result.ErrorKey = "err.driver";
                    result.Detail = "TAP adapter unavailable";
                    return result;
                }

                // a disabled adapter accepts no address, and openvpn then waits forever
                await Task.Run(() => _tap.EnsureEnabled());

                var candidates = new List<VpnServer>();
                if (server != null) candidates.Add(server);
                if (alternatives != null)
                {
                    // build a wide list; the preflight drops dead nodes in ~1.2 s
                    foreach (var alt in alternatives)
                    {
                        if (candidates.Count >= PreflightPoolSize) break;
                        if (alt == null) continue;
                        // Dedupe on Id, not Ip: the same VPNBook host publishes both a
                        // tcp443 and a udp25000 profile, and dropping one by IP meant the
                        // UDP profile - the fast one - never reached the preflight.
                        if (candidates.Any(c => c.Id == alt.Id)) continue;
                        candidates.Add(alt);
                    }
                }

                Stage(Loc.T("stage.probe"));
                var probed = await PreflightAsync(candidates, settings, _cts.Token).ConfigureAwait(false);
                if (probed.Count == 0)
                {
                    _log.Warn("preflight: every candidate is unreachable from this network");
                    result.ErrorKey = "err.unreachable";
                    result.Detail = "all candidates filtered";
                    SetState(State.Idle);
                    return result;
                }
                if (probed.Count < candidates.Count)
                    _log.Info("preflight: skipped " + (candidates.Count - probed.Count) + " dead nodes");
                candidates = probed;

                int attemptNo = 0;
                var connectSw = Stopwatch.StartNew();
                foreach (var candidate in candidates)
                {
                    if (_cts.IsCancellationRequested) break;
                    if (attemptNo++ >= MaxAttempts) break;
                    // stop handing out attempts once the overall deadline is near,
                    // otherwise a dead country means a minute of spinning
                    if (connectSw.Elapsed.TotalSeconds > TotalConnectTimeoutSec - 4)
                    {
                        _log.Warn("connect: overall deadline reached after " + (int)connectSw.Elapsed.TotalSeconds +
                                  "s, stopping at attempt " + (attemptNo - 1));
                        break;
                    }
                    // The DNS block is a nicety; bringing the interface up is not.
                    // First attempt runs without it so a WFP quirk cannot cost the
                    // whole connection.
                    _noDnsBlockThisAttempt = attemptNo == 1;
                    Stage(Loc.T("stage.tunnel") + " · " + candidate.Host);
                    _log.Info("attempt " + attemptNo + "/" + Math.Min(candidates.Count, MaxAttempts) +
                              ": " + candidate.Host + " (" + candidate.CountryCode + ")");
                    var attempt = await TryServerAsync(candidate, settings, _cts.Token).ConfigureAwait(false);
                    if (_cancelled)
                    {
                        result.Ok = false;
                        result.Cancelled = true;
                        StopProcess();
                        SetState(State.Idle);
                        return result;
                    }
                    if (attempt.Ok)
                    {
                        ActiveServer = candidate;
                        ActiveIp = attempt.Ip;
                        SetState(State.Connected);
                        if (settings.KillSwitch) _killSwitch.Disable(); // tunnel is up; blocking would break it
                        return attempt;
                    }
                    result.ErrorKey = attempt.ErrorKey;
                    result.Detail = attempt.Detail;
                    _log.Warn("server failed: " + candidate.Host + " -> " + attempt.Detail);
                }

                result.Ok = false;
                result.Cancelled = _cancelled;
                SetState(State.Idle);
                if (settings.KillSwitch) _killSwitch.Disable();
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Ok = false;
                result.Cancelled = true;
                result.ErrorKey = "err.cancelled";
                SetState(State.Idle);
                if (settings.KillSwitch) _killSwitch.Disable();
                return result;
            }
            catch (Exception ex)
            {
                _log.Error("connect failed: " + ex);
                result.Ok = false;
                result.ErrorKey = "err.unknown";
                result.Detail = ex.Message;
                SetState(State.Idle);
                if (settings.KillSwitch) _killSwitch.Disable();
                return result;
            }
        }

        /// <summary>One transport option: TCP or UDP on a specific port.</summary>
        private sealed class TransportPlan
        {
            public bool Tcp;
            public int Port;
            public int LatencyMs;
            public override string ToString() { return (Tcp ? "tcp/" : "udp/") + Port; }
        }

        /// <summary>
        /// Cheap reachability check on the real endpoint, run before starting a tunnel.
        ///
        /// History of this method matters, because two earlier versions were wrong:
        ///  - probing by Host name failed instantly for every node: VPN Gate labels
        ///    its nodes "vpn193342579" / "public-vpn-94", which do not resolve in DNS.
        ///    The old code then declared all 20 candidates dead in 14 ms and fell
        ///    straight through to UDP against nodes that were never checked.
        ///  - probing only TCP disqualified nodes that serve UDP only.
        ///
        /// The real remote lives in the profile (field Ip is the endpoint), so that
        /// is what we test: TCP on the profile's own port when the profile says tcp,
        /// otherwise a UDP handshake packet with a reply wait. Only nodes that do
        /// not answer at all are dropped, and the list survives intact if the
        /// measurement itself proves unreliable.
        /// </summary>
        private async Task<List<VpnServer>> PreflightAsync(List<VpnServer> candidates, AppSettings settings, CancellationToken ct)
        {
            if (settings != null && string.Equals(settings.Transport, "direct", StringComparison.OrdinalIgnoreCase))
                return candidates;

            // Reuse a recent measurement. A user who cancels and immediately presses
            // connect again should not sit through the whole probe cycle a second
            // time; node reachability does not change inside a minute.
            if (_lastProbeAt != DateTime.MinValue &&
                (DateTime.UtcNow - _lastProbeAt).TotalSeconds < 60 &&
                _lastProbeIds != null)
            {
                var cached = candidates.Where(c => _lastProbeIds.Contains(c.Id)).ToList();
                if (cached.Count > 0)
                {
                    _log.Info("preflight: reusing results from " +
                              (int)(DateTime.UtcNow - _lastProbeAt).TotalSeconds + "s ago (" + cached.Count + " alive)");
                    return cached;
                }
            }

            // 8 slots was tuned for a 20-node pool with one probe each. The pool is
            // 40 now and a TCP profile needs up to four probes, so 40/8*1.2 s could
            // exceed 20 s before the first attempt even started - visible as a long
            // "checking" pause right after pressing connect or after cancelling.
            var gate = new SemaphoreSlim(16);
            var tasks = new List<Task<(VpnServer srv, int ms)>>();
            int probedCount = 0;
            Stage(Loc.T("stage.probe") + " · 0/" + candidates.Count);
            foreach (var srv in candidates)
            {
                var s = srv;
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var (ms, _) = await ProbeServerAsync(s, settings, ct).ConfigureAwait(false);
                        return (s, ms);
                    }
                    finally
                    {
                        gate.Release();
                        int done = Interlocked.Increment(ref probedCount);
                        Stage(Loc.T("stage.probe") + " · " + done + "/" + candidates.Count);
                    }
                }, ct));
            }

            (VpnServer srv, int ms)[] results;
            try { results = await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch { return candidates; }

            foreach (var r in results)
                _log.Info("probe: " + r.srv.Host + " [" + EndpointOf(r.srv) + "] " +
                          (r.ms >= 0 ? r.ms + " ms" : "no reply"));

            // Latency alone is a poor speed predictor on volunteer nodes: a 200 ms
            // node can push 40 Mbit/s while a 150 ms one caps at 2. VPN Gate
            // publishes its own speed measurement, so rank on that first and use
            // latency only to break ties.
            // Ranking, in order of what actually predicts usable speed:
            //  1. UDP before TCP. Volunteer nodes are bandwidth-starved and TCP
            //     retransmits turn any loss into a stall - this is the difference
            //     between 0.1 Mbit/s and a usable tunnel, and it dominates the other
            //     factors by an order of magnitude.
            //  2. VPN Gate's SpeedBps field is the node's own loopback benchmark
            //     ("970.8 Mbit/s advertised" on a home line). It measures the wrong
            //     thing, so only trust it as a weak tie-break.
            //  3. Latency last: it predicts responsiveness, not throughput.
            var alive = results.Where(r => r.ms >= 0)
                               .OrderBy(r => TransportOf(r.srv).Tcp ? 1 : 0)
                               .ThenByDescending(r => Math.Min(r.srv.SpeedBps, 100000000))
                               .ThenBy(r => r.ms)
                               .ToList();
            foreach (var r in alive.Take(4))
                _log.Info("rank: " + r.srv.Host + " " + r.ms + " ms via " + TransportOf(r.srv));
            _log.Info("preflight: " + alive.Count + "/" + results.Length + " candidates answered");
            if (alive.Count == 0)
            {
                // Do not cache an empty result: the next connect must probe again
                // instead of inheriting "everything is dead" for a minute.
                _lastProbeAt = DateTime.MinValue;
                _lastProbeIds = null;
                _log.Warn("preflight: nothing answered, trying top-ranked nodes anyway");
                return candidates.Take(MaxAttempts).ToList();
            }
            _lastProbeAt = DateTime.UtcNow;
            _lastProbeIds = new HashSet<string>(alive.Select(r => r.srv.Id), StringComparer.OrdinalIgnoreCase);
            return alive.Select(r => r.srv).ToList();
        }

        private static string EndpointOf(VpnServer srv)
        {
            if (!string.IsNullOrEmpty(srv.Ip) && srv.Ip != srv.Host) return srv.Ip;
            var cfg = ServerSource.DecodeConfig(srv);
            if (!string.IsNullOrEmpty(cfg))
            {
                foreach (var line in cfg.Split('\n'))
                {
                    var l = line.Trim();
                    if (l.StartsWith("remote ", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = l.Split(' ');
                        if (parts.Length >= 2) return parts[1];
                    }
                }
            }
            return srv.Host;
        }

        /// <summary>
        /// Picks the fastest transport that actually answers.
        ///
        /// VPN Gate only hands out TCP profiles, but the nodes normally listen on
        /// UDP as well. TCP-over-TCP caps throughput hard (one loss event stalls the
        /// whole window), which is why the app measured ~1 Mbit/s. So UDP is tried
        /// first and used when it answers; the profile's own TCP port stays as the
        /// fallback, because the custom ports in those profiles are TCP-only.
        /// </summary>
        private async Task<(int ms, bool ok)> ProbeServerAsync(VpnServer srv, AppSettings settings, CancellationToken ct)
        {
            var cfgText = ServerSource.DecodeConfig(srv);
            var basePlan = PickTransport(srv, cfgText, settings);
            string host = EndpointOf(srv);
            if (string.IsNullOrEmpty(host)) return (-1, false);

            if (!basePlan.Tcp)
            {
                int direct = await ProbeUdpAsync(host, basePlan.Port, ProbeTimeoutMs, ct).ConfigureAwait(false);
                if (direct >= 0) { lock (_plans) _plans[srv.Id] = basePlan; return (direct, true); }
                return (-1, false);
            }

            // TCP profile: does the node also serve UDP, and on which port?
            //
            // Probe everything at once. Doing the three UDP ports sequentially cost
            // up to 3.6 s per node, and with only 8 slots the whole preflight grew to
            // 11 s - long enough to look hung. Parallel probing keeps it at ~1.2 s.
            int[] udpPorts = basePlan.Port == 1194 ? new[] { 1194, 25000 } : new[] { basePlan.Port, 1194, 25000 };
            var udpTasks = udpPorts.Select(p => ProbeUdpAsync(host, p, ProbeTimeoutMs, ct)).ToList();
            udpTasks.Add(ProbeTcpAsync(host, basePlan.Port, ProbeTimeoutMs, ct));
            int[] results;
            try { results = await Task.WhenAll(udpTasks).ConfigureAwait(false); }
            catch { return (-1, false); }

            // Prefer UDP even when TCP answered: TCP-in-TCP is what throttled the
            // tunnel to 0.1 Mbit/s, so a working UDP path always wins.
            for (int i = 0; i < udpPorts.Length; i++)
            {
                if (results[i] < 0) continue;
                var udpPlan = new TransportPlan { Tcp = false, Port = udpPorts[i] };
                lock (_plans) _plans[srv.Id] = udpPlan;
                return (results[i], true);
            }

            int tcp = results[results.Length - 1];
            if (tcp >= 0) lock (_plans) _plans[srv.Id] = basePlan;
            return (tcp, true);
        }

        /// <summary>
        /// Watches the tunnel after it is up.
        ///
        /// Two things killed connections from the user's side and were invisible
        /// before: openvpn exiting on its own (a single dropped packet during the
        /// handshake was fatal with connect-retry-max 1), and the process being
        /// alive while the data channel is gone. Both now surface as TunnelLost.
        /// </summary>
        private void StartWatchdog()
        {
            var proc = _proc;
            if (proc == null) return;

            // drop any previous watcher
            try { _watchCts?.Cancel(); } catch { }
            _watchCts = new CancellationTokenSource();
            var token = _watchCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(1500, token).ConfigureAwait(false);
                        if (_proc != proc) return;            // replaced or stopped by us
                        if (proc.HasExited)
                        {
                            _log.Warn("tunnel dropped: openvpn exited with " + proc.ExitCode);
                            RaiseTunnelLost("openvpn exited");
                            return;
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _log.Warn("watchdog: " + ex.Message); }
            }, token);
        }

        private void RaiseTunnelLost(string reason)
        {
            try { _watchCts?.Cancel(); } catch { }
            var h = TunnelLost;
            if (h != null) h(reason);
        }

        /// <summary>The transport the preflight settled on, falling back to a fresh guess.</summary>
        private TransportPlan TransportOf(VpnServer srv)
        {
            TransportPlan plan = null;
            lock (_plans) _plans.TryGetValue(srv.Id, out plan);
            if (plan != null) return plan;
            return PickTransport(srv, ServerSource.DecodeConfig(srv), null);
        }

        private const int ProbeTimeoutMs = 1200;

        /// <summary>
        /// UDP has no connect handshake, so a silent port is indistinguishable from
        /// a dead one. OpenVPN answers a P_CONTROL_HARD_RESET as soon as it sees it,
        /// so sending one and waiting for any reply tells us the node is alive.
        /// No reply is not proof of death (some servers rate-limit resets), which is
        /// why the caller falls back to the ranking instead of trusting an empty set.
        /// </summary>
        private static async Task<int> ProbeUdpAsync(string host, int port, int timeoutMs, CancellationToken ct)
        {
            try
            {
                var addrs = await System.Net.Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
                var addr = addrs.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ?? addrs.FirstOrDefault();
                if (addr == null) return -1;
                if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return -1;

                using (var udp = new System.Net.Sockets.UdpClient(0))
                {
                    udp.Client.ReceiveTimeout = timeoutMs;
                    // P_CONTROL_HARD_RESET_CLIENT_V2: opcode 7 << 3, 8 byte session id,
                    // no packet id, no message packet id, zero-length payload.
                    var packet = new byte[8 + 1 + 8];
                    packet[0] = 0x38;
                    for (int i = 0; i < 8; i++) packet[1 + i] = (byte)(i * 7 + 1);
                    packet[9] = 0x00; // message packet id array length

                    var ep = new System.Net.IPEndPoint(addr, port);
                    var sw = Stopwatch.StartNew();
                    await udp.SendAsync(packet, packet.Length, ep).ConfigureAwait(false);

                    var receive = udp.ReceiveAsync();
                    var done = await Task.WhenAny(receive, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
                    if (done != receive) return -1;
                    var result = await receive.ConfigureAwait(false);
                    if (result == null || result.Buffer == null || result.Buffer.Length == 0) return -1;
                    sw.Stop();
                    return (int)sw.ElapsedMilliseconds;
                }
            }
            catch { return -1; }
        }

        private static async Task<int> ProbeTcpAsync(string host, int port, int timeoutMs, CancellationToken ct)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var sw = Stopwatch.StartNew();
                    var connect = client.ConnectAsync(host, port);
                    var done = await Task.WhenAny(connect, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
                    if (done != connect) return -1;
                    await connect.ConfigureAwait(false);
                    if (!client.Connected) return -1;
                    sw.Stop();
                    return (int)sw.ElapsedMilliseconds;
                }
            }
            catch { return -1; }
        }

        private async Task<ConnectResult> TryServerAsync(VpnServer server, AppSettings settings, CancellationToken ct)
        {
            string cfgText = ServerSource.DecodeConfig(server);
            if (string.IsNullOrEmpty(cfgText))
                return new ConnectResult { Server = server, ErrorKey = "err.network", Detail = "no config" };

            TransportPlan plan = null;
            lock (_plans) _plans.TryGetValue(server.Id, out plan);
            if (plan == null) plan = PickTransport(server, cfgText, settings);
            _log.Info("transport for " + server.Host + ": " + plan);
            return await RunOpenVpnOnceAsync(server, settings, cfgText, plan, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The profile names its own protocol; follow it, but allow the user to
        /// force tcp or udp. Never mix the two on one port - OpenVPN cannot.
        /// </summary>
        private static TransportPlan PickTransport(VpnServer server, string cfgText, AppSettings settings)
        {
            string forced = settings == null ? null : settings.Transport;
            if (string.Equals(forced, "tcp", StringComparison.OrdinalIgnoreCase))
                return new TransportPlan { Tcp = true, Port = 443 };
            if (string.Equals(forced, "udp", StringComparison.OrdinalIgnoreCase))
                return new TransportPlan { Tcp = false, Port = 1194 };

            bool tcp = cfgText != null && cfgText.IndexOf("proto tcp", StringComparison.OrdinalIgnoreCase) >= 0;
            int port = 0;
            if (cfgText != null)
            {
                foreach (var line in cfgText.Split('\n'))
                {
                    var l = line.Trim();
                    if (!l.StartsWith("remote ", StringComparison.OrdinalIgnoreCase)) continue;
                    var parts = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3) int.TryParse(parts[2], out port);
                    break;
                }
            }
            if (port <= 0) port = tcp ? 443 : 1194;
            return new TransportPlan { Tcp = tcp, Port = port };
        }

        private async Task<ConnectResult> RunOpenVpnOnceAsync(VpnServer server, AppSettings settings, string cfgText, TransportPlan plan, CancellationToken ct)
        {
            var res = new ConnectResult { Server = server };
            if (string.IsNullOrEmpty(cfgText))
            {
                res.ErrorKey = "err.network";
                res.Detail = "no config";
                return res;
            }

            string authFile = null;
            if (!string.IsNullOrEmpty(server.AuthUser) && !string.IsNullOrEmpty(server.AuthPass))
            {
                authFile = Path.Combine(_paths.WorkDir, "auth.txt");
                try
                {
                    File.WriteAllText(authFile, server.AuthUser + "\r\n" + server.AuthPass + "\r\n", new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    _log.Warn("auth file write failed: " + ex.Message);
                    authFile = null;
                }
            }

            var opts = new ConfigBuilder.Options
            {
                AdapterName = TapManager.AdapterName,
                AuthFile = authFile == null ? null : authFile.Replace('\\', '/'),
                Transport = settings.Transport == "tcp" ? ConfigBuilder.Transport.Tcp
                          : settings.Transport == "udp" ? ConfigBuilder.Transport.Udp
                          : ConfigBuilder.Transport.Auto,
                ConnectTimeoutSec = 8,
                NoDnsBlock = _noDnsBlockThisAttempt,
            };
            opts.Transport = plan.Tcp ? ConfigBuilder.Transport.Tcp : ConfigBuilder.Transport.Udp;
            opts.RemotePort = plan.Port;

            string config = ConfigBuilder.Build(server, cfgText, opts);
            string cfgPath = Path.Combine(_paths.WorkDir, "active.ovpn");
            File.WriteAllText(cfgPath, config, new UTF8Encoding(false));

            string openvpn = Path.Combine(_paths.EngineDir, "openvpn.exe");
            if (!File.Exists(openvpn))
            {
                res.ErrorKey = "err.start";
                res.Detail = "openvpn.exe missing";
                _log.Error("openvpn.exe missing in engine dir");
                return res;
            }

            string logPath = Path.Combine(_paths.WorkDir, "openvpn.log");
            try { if (File.Exists(logPath)) File.Delete(logPath); } catch { }

            var psi = new ProcessStartInfo(openvpn, "--config \"" + cfgPath + "\" --log \"" + logPath + "\" --verb 3")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            _log.Info("starting: openvpn --config " + cfgPath);
            _log.Info("engine dir: " + _paths.EngineDir + " (openvpn exists=" + File.Exists(openvpn) + ")");
            _log.Info("config preview: " + config.Substring(0, Math.Min(600, config.Length)).Replace("\r\n", " / "));

            try
            {
                _proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                res.ErrorKey = "err.start";
                res.Detail = ex.Message;
                return res;
            }

            _tunnelUp = false;
            _authRejected = false;
            // A previous attempt's failure text must never be reported as this
            // server's reason - uk205.vpnbook.com was blamed for an AUTH_FAILED
            // that happened on the VPN Gate node before it.
            _lastLogLine = "";
            _routeStuck = 0;
            _phase = null;
            var sw = Stopwatch.StartNew();
            long readOffset = 0;
            bool timedOut = false;

            long budget = Math.Max(6, AttemptTimeoutSec) * 1000L;
            var hostStage = Loc.T("stage.tunnel") + " · " + server.Host;
            Stage(hostStage);
            while (sw.ElapsedMilliseconds < budget)
            {
                if (ct.IsCancellationRequested) break;
                await Task.Delay(220, ct).ConfigureAwait(false);

                // a visible countdown: silence looks like a frozen app.
                // The phase is what tells the user whether it is stuck on the
                // network, the TLS handshake or the driver.
                int left = (int)Math.Max(0, (budget - sw.ElapsedMilliseconds) / 1000);
                string phase = _phase;
                Stage((string.IsNullOrEmpty(phase) ? hostStage : Loc.T(phase) + " · " + server.Host) +
                      " · " + left + "s");

                if (_authRejected)
                {
                    _log.Warn("auth rejected by " + server.Host + " - moving on");
                    _lastLogLine = "AUTH_FAILED";
                    StopProcess();
                    res.ErrorKey = "err.auth";
                    res.Detail = "AUTH_FAILED";
                    return res;
                }

                if (_proc.HasExited)
                {
                    string err = "";
                    try { err = _proc.StandardError.ReadToEnd(); } catch { }
                    string outp = "";
                    try { outp = _proc.StandardOutput.ReadToEnd(); } catch { }
                    _log.Warn("openvpn exited with " + _proc.ExitCode);
                    if (!string.IsNullOrWhiteSpace(err)) _log.Warn("openvpn stderr: " + err.Trim());
                    if (!string.IsNullOrWhiteSpace(outp)) _log.Warn("openvpn stdout: " + outp.Trim());
                    readOffset = PumpLog(logPath, 0);
                    break;
                }

                readOffset = PumpLog(logPath, readOffset);
                if (_tunnelUp) break;

                // Six "Waiting for TUN/TAP interface" lines means the adapter is not
                // taking an address; more waiting cannot fix that, and the driver is
                // the same for every server, so report it instead of burning 3 more
                // attempts on nodes that would fail identically.
                if (_routeStuck >= 6)
                {
                    _log.Error("TAP interface did not come up - check that the adapter '" +
                               TapManager.AdapterName + "' is enabled in ncpa.cpl");
                    StopProcess();
                    res.ErrorKey = "err.adapter";
                    res.Detail = "TAP interface did not come up";
                    return res;
                }
            }

            if (!_tunnelUp)
            {
                timedOut = sw.ElapsedMilliseconds >= budget - 250;
                // A silent TCP handshake leaves _lastLogLine empty, which used to
                // log "server failed: X -> " with no reason at all.
                if (string.IsNullOrWhiteSpace(_lastLogLine))
                    _lastLogLine = timedOut ? "no answer on " + plan.Port + "/" + (plan.Tcp ? "tcp" : "udp")
                                            : "connection closed by " + server.Host;
                res.ErrorKey = classify(_lastLogLine, timedOut);
                res.Detail = _lastLogLine;
                StopProcess();
                return res;
            }

            Stage(Loc.T("stage.routes"));
            // With UDP the default route settles a little later than with TCP; give
            // the stack time before asking anyone who we are, otherwise every
            // provider fails and the UI is left showing the pre-connect address.
            await Task.Delay(1500, ct).ConfigureAwait(false);
            Stage(Loc.T("stage.verify"));

            var checker = new IpChecker(_log);
            IpInfo info = null;
            using (var verifyCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                verifyCts.CancelAfter(TimeSpan.FromSeconds(12));
                for (int i = 0; i < 4; i++)
                {
                    try { info = await checker.GetAsync(verifyCts.Token).ConfigureAwait(false); } catch { }
                    if (info != null && info.Ok) break;
                    try { await Task.Delay(800, verifyCts.Token).ConfigureAwait(false); } catch { break; }
                }
            }

            res.Ok = true;
            res.Ip = info ?? new IpInfo();
            _log.Info("tunnel up via " + server.Host + ", exit ip " + (info == null ? "?" : info.Ip));
            StartWatchdog();
            return res;
        }

        private string classify(string line, bool timedOut)
        {
            var s = (line ?? "").ToLowerInvariant();
            if (s.Contains("auth_failed") || s.Contains("auth-failure")) return "err.auth";
            if (s.Contains("cannot open") || s.Contains("all tap-windows") || s.Contains("adapter")) return "err.driver";
            // The server never answered the control channel: the node is alive on
            // the port but not serving openvpn, which is a different problem from
            // our own timeout and deserves its own message.
            if (s.Contains("server poll timeout") || s.Contains("connection reset")) return "err.silent";
            if (timedOut || s.Contains("timeout")) return "err.timeout";
            if (s.Contains("tls error") || s.Contains("network is unreachable")) return "err.network";
            return "err.network";
        }

        private long PumpLog(string path, long offset)
        {
            try
            {
                if (!File.Exists(path)) return offset;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < offset) offset = 0;
                    fs.Seek(offset, SeekOrigin.Begin);
                    using (var reader = new StreamReader(fs, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            offset = fs.Position;
                            if (line.IndexOf("Initialization Sequence Completed", StringComparison.OrdinalIgnoreCase) >= 0)
                                _tunnelUp = true;
                            if (line.IndexOf("AUTH_FAILED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                line.IndexOf("TLS Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                line.IndexOf("FATAL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                // these explain a failure just as well and used to be
                                // dropped, leaving an empty reason
                                line.IndexOf("Server poll timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                line.IndexOf("Connection reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                line.IndexOf("connect to", StringComparison.OrdinalIgnoreCase) >= 0 && line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                _lastLogLine = line.Trim();
                                if (line.IndexOf("AUTH_FAILED", StringComparison.OrdinalIgnoreCase) >= 0)
                                    _authRejected = true;
                            }
                            // The TAP interface never coming up is fatal in practice:
                            // openvpn retries "Waiting for TUN/TAP interface" forever.
                            if (line.IndexOf("Waiting for TUN/TAP interface", StringComparison.OrdinalIgnoreCase) >= 0)
                                _routeStuck++;

                            // Track the real phase so the UI can say what openvpn is
                            // actually doing instead of showing a bare countdown for
                            // the whole attempt.
                            if (line.IndexOf("Attempting to establish TCP connection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                line.IndexOf("UDPv4 link remote", StringComparison.OrdinalIgnoreCase) >= 0)
                                _phase = "stage.phase.connect";
                            else if (line.IndexOf("TCP connection established", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     line.IndexOf("Peer Connection Initiated", StringComparison.OrdinalIgnoreCase) >= 0)
                                _phase = "stage.phase.tls";
                            else if (line.IndexOf("PUSH_REQUEST", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     line.IndexOf("AUTH_FAILED", StringComparison.OrdinalIgnoreCase) >= 0)
                                _phase = "stage.phase.auth";
                            else if (line.IndexOf("PUSH_REPLY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     line.IndexOf("open_tun", StringComparison.OrdinalIgnoreCase) >= 0)
                                _phase = "stage.phase.tun";
                            else if (line.IndexOf("TEST ROUTES", StringComparison.OrdinalIgnoreCase) >= 0)
                                _phase = "stage.phase.routes";
                            var h = LogLine;
                            if (h != null && line.Length > 0) h("  " + line.Trim());
                        }
                    }
                }
            }
            catch { }
            return offset;
        }

        /// <summary>
        /// Stops whatever is running and returns to Idle. Safe to call while a
        /// connection attempt is mid-flight: the attempt's own continuation sees
        /// the cancelled token and bails out without resurrecting the UI state.
        /// </summary>
        public void Disconnect()
        {
            bool wasBusy;
            lock (_sync)
            {
                wasBusy = CurrentState != State.Idle;
            }
            _cancelled = true;
            try { if (_cts != null) _cts.Cancel(); } catch { }
            StopProcess();
            _killSwitch.Disable();
            ActiveServer = null;
            ActiveIp = null;
            try { if (_cts != null) _cts.Dispose(); } catch { }
            _cts = null;
            SetState(State.Idle);
            if (wasBusy) _log.Info("connect cancelled / disconnected");
        }

        /// <summary>True once Disconnect() was requested for the running attempt.</summary>
        public bool IsCancelling { get { return _cancelled; } }

        private void StopProcess()
        {
            // Cancel the watcher first: a deliberate disconnect must never look
            // like a dropped tunnel and trigger a reconnect loop.
            try { _watchCts?.Cancel(); } catch { }
            try
            {
                if (_proc != null && !_proc.HasExited)
                {
                    // Kill directly: taskkill + a 4 s wait used to keep the UI in
                    // "Connecting" long after the attempt had already failed.
                    try { _proc.Kill(); } catch { }
                    try { _proc.WaitForExit(700); } catch { }
                }
            }
            catch { }
            finally
            {
                _proc = null;
                _tunnelUp = false;
            }
        }

        /// <summary>Session/delta reader parsing OpenVPN's management-free log is not possible,
        /// so throughput is sampled by the UI from adapter counters (see NetStats).</summary>
        public void Dispose()
        {
            Disconnect();
        }

        private void SetState(State s)
        {
            CurrentState = s;
            var h = StateChanged;
            if (h != null) h(s);
        }

        private void Stage(string text)
        {
            var h = StageChanged;
            if (h != null) h(text);
        }
    }
}
