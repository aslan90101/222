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
        private CancellationTokenSource _cts;
        private volatile string _lastLogLine = "";
        private volatile bool _tunnelUp;

        public State CurrentState { get; private set; }
        public VpnServer ActiveServer { get; private set; }
        public IpInfo ActiveIp { get; private set; }

        public event Action<State> StateChanged;
        public event Action<string> StageChanged;
        public event Action<string> LogLine;

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
        public int AttemptTimeoutSec = 12;

        /// <summary>Hard cap on attempts in one ConnectAsync call.</summary>
        public int MaxAttempts = 3;

        /// <summary>How many ranked servers enter the preflight (cheap, parallel).</summary>
        public int PreflightPoolSize = 20;

        /// <summary>Set by the preflight: did any candidate answer on TCP?</summary>
        private bool _preflightFoundTcp = true;

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

                var candidates = new List<VpnServer>();
                if (server != null) candidates.Add(server);
                if (alternatives != null)
                {
                    // build a wide list; the preflight drops dead nodes in ~1.2 s
                    foreach (var alt in alternatives)
                    {
                        if (candidates.Count >= PreflightPoolSize) break;
                        if (alt == null) continue;
                        if (candidates.Any(c => c.Ip == alt.Ip)) continue;
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
                foreach (var candidate in candidates)
                {
                    if (_cts.IsCancellationRequested) break;
                    if (attemptNo++ >= MaxAttempts) break;
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
        /// Measures a whole candidate list in parallel before any tunnel is started.
        ///
        /// The reported dead end was exactly this: three KR nodes, each burning a
        /// full 15 s attempt on "Attempting to establish TCP connection", ending in
        /// "not connected" after ~45 s of nothing. A TCP connect either completes
        /// in a few hundred ms or the port is filtered - probing all 20 candidates
        /// concurrently costs ~1.2 s total and tells us who is worth trying.
        /// </summary>
        private async Task<List<VpnServer>> PreflightAsync(List<VpnServer> candidates, AppSettings settings, CancellationToken ct)
        {
            if (settings != null && string.Equals(settings.Transport, "direct", StringComparison.OrdinalIgnoreCase))
                return candidates;

            var gate = new SemaphoreSlim(8);
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
                        int best = int.MaxValue;
                        foreach (int port in ProbePorts)
                        {
                            int ms = await ProbeTcpAsync(s.Host, port, ProbeTimeoutMs, ct).ConfigureAwait(false);
                            if (ms >= 0 && ms < best) best = ms;
                        }
                        return (s, best == int.MaxValue ? -1 : best);
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

            var alive = results.Where(r => r.ms >= 0).OrderBy(r => r.ms).ToList();
            foreach (var r in results)
                _log.Info("probe: " + r.srv.Host + " " + (r.ms >= 0 ? r.ms + " ms" : "unreachable"));
            _log.Info("preflight: " + alive.Count + "/" + results.Length + " candidates answered");

            _preflightFoundTcp = alive.Count > 0;
            if (alive.Count == 0)
            {
                // No TCP answer anywhere. That can mean the whole range is dead,
                // or that this network filters TCP while UDP still flows - which
                // is common for VPN traffic. Fall back to the best-ranked nodes
                // over UDP rather than declaring the network hopeless.
                _log.Warn("preflight: no TCP answer, falling back to UDP on top-ranked nodes");
                return candidates.Take(MaxAttempts).ToList();
            }
            return alive.Select(r => r.srv).ToList();
        }

        private const int ProbeTimeoutMs = 1200;
        private static readonly int[] ProbePorts = { 443, 1194, 25000 };

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

            var plan = PickTransport(server, cfgText, settings);
            if (_preflightFoundTcp == false && plan.Tcp)
            {
                // nothing answered on TCP during preflight; do not repeat it
                plan = new TransportPlan { Tcp = false, Port = 1194 };
                _log.Info("transport for " + server.Host + ": switching to " + plan + " (no TCP answer)");
            }
            else
            {
                _log.Info("transport for " + server.Host + ": " + plan);
            }
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
            return new TransportPlan { Tcp = tcp, Port = tcp ? 443 : 1194 };
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

                // a visible countdown: silence looks like a frozen app
                int left = (int)Math.Max(0, (budget - sw.ElapsedMilliseconds) / 1000);
                Stage(hostStage + " · " + left + "s");

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
            await Task.Delay(900, ct).ConfigureAwait(false);
            Stage(Loc.T("stage.verify"));

            var checker = new IpChecker(_log);
            IpInfo info = null;
            using (var verifyCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                verifyCts.CancelAfter(TimeSpan.FromSeconds(8));
                for (int i = 0; i < 2; i++)
                {
                    try { info = await checker.GetAsync(verifyCts.Token).ConfigureAwait(false); } catch { }
                    if (info != null && info.Ok) break;
                    try { await Task.Delay(500, verifyCts.Token).ConfigureAwait(false); } catch { break; }
                }
            }

            res.Ok = true;
            res.Ip = info ?? new IpInfo();
            _log.Info("tunnel up via " + server.Host + ", exit ip " + (info == null ? "?" : info.Ip));
            return res;
        }

        private string classify(string line, bool timedOut)
        {
            var s = (line ?? "").ToLowerInvariant();
            if (s.Contains("auth_failed") || s.Contains("auth-failure")) return "err.auth";
            if (s.Contains("cannot open") || s.Contains("all tap-windows") || s.Contains("adapter")) return "err.driver";
            if (timedOut || s.Contains("timeout")) return "err.timeout";
            if (s.Contains("tls error") || s.Contains("reset") || s.Contains("network is unreachable")) return "err.network";
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
                                line.IndexOf("FATAL", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                _lastLogLine = line.Trim();
                                if (line.IndexOf("AUTH_FAILED", StringComparison.OrdinalIgnoreCase) >= 0)
                                    _authRejected = true;
                            }
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
