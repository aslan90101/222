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
        public int AttemptTimeoutSec = 30;

        /// <summary>Hard cap on attempts in one ConnectAsync call.</summary>
        public int MaxAttempts = 4;

        public async Task<ConnectResult> ConnectAsync(VpnServer server, AppSettings settings, IList<VpnServer> alternatives, CancellationToken ct)
        {
            lock (_sync)
            {
                if (CurrentState == State.Connected || CurrentState == State.Connecting)
                    return new ConnectResult { Ok = false, Detail = "already busy" };
                SetState(State.Connecting);
            }
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

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
                    foreach (var alt in alternatives)
                    {
                        if (candidates.Count >= 3) break;
                        if (alt == null) continue;
                        if (candidates.Any(c => c.Ip == alt.Ip)) continue;
                        candidates.Add(alt);
                    }
                }

                int attemptNo = 0;
                foreach (var candidate in candidates)
                {
                    if (_cts.IsCancellationRequested) break;
                    if (attemptNo++ >= MaxAttempts) break;
                    Stage(Loc.T("stage.tunnel") + " · " + candidate.Host);
                    _log.Info("attempt " + attemptNo + ": " + candidate.Host + " (" + candidate.CountryCode + ")");
                    var attempt = await TryServerAsync(candidate, settings, _cts.Token).ConfigureAwait(false);
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
                SetState(State.Idle);
                if (settings.KillSwitch) _killSwitch.Disable();
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Ok = false;
                result.ErrorKey = "err.timeout";
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

        private async Task<ConnectResult> TryServerAsync(VpnServer server, AppSettings settings, CancellationToken ct)
        {
            var res = new ConnectResult { Server = server };
            string cfgText = ServerSource.DecodeConfig(server);
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
                ConnectTimeoutSec = 20,
            };
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
            var sw = Stopwatch.StartNew();
            long readOffset = 0;
            bool timedOut = false;

            long budget = Math.Max(10, AttemptTimeoutSec) * 1000L;
            while (sw.ElapsedMilliseconds < budget)
            {
                if (ct.IsCancellationRequested) break;
                await Task.Delay(220, ct).ConfigureAwait(false);

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
                timedOut = sw.ElapsedMilliseconds >= budget;
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
            for (int i = 0; i < 3; i++)
            {
                info = await checker.GetAsync(ct).ConfigureAwait(false);
                if (info != null && info.Ok) break;
                await Task.Delay(700, ct).ConfigureAwait(false);
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
                                _lastLogLine = line.Trim();
                            var h = LogLine;
                            if (h != null && line.Length > 0) h("  " + line.Trim());
                        }
                    }
                }
            }
            catch { }
            return offset;
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                if (CurrentState == State.Idle) return;
                SetState(State.Disconnecting);
            }
            try { if (_cts != null) _cts.Cancel(); } catch { }
            StopProcess();
            _killSwitch.Disable();
            ActiveServer = null;
            ActiveIp = null;
            SetState(State.Idle);
            _log.Info("disconnected");
        }

        private void StopProcess()
        {
            try
            {
                if (_proc != null && !_proc.HasExited)
                {
                    // Ask politely first so OpenVPN removes its routes, then force.
                    try
                    {
                        var psi = new ProcessStartInfo("taskkill", "/PID " + _proc.Id + " /T /F")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        };
                        Process.Start(psi);
                    }
                    catch { }
                    if (!_proc.WaitForExit(4000)) { try { _proc.Kill(); } catch { } }
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
