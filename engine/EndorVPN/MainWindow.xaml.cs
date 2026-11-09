using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Controls.Primitives;
using System.ComponentModel;
using System.Windows.Threading;
using EndorVPN.Core;
using EndorVPN.UI;

namespace EndorVPN
{
    public partial class MainWindow : Window
    {
        private readonly ServerBook _book;
        private readonly ServerCache _cache;
        private readonly KillSwitch _killSwitch;
        private readonly VpnEngine _engine;
        private readonly IpChecker _ipChecker;
        private readonly NetStats _netStats;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _uiTimer;
        private readonly DispatcherTimer _toastTimer;

        private CancellationTokenSource _opCts;
        private bool _busy;
        private bool _closing;
        private bool _minimizeToTray;
        private DateTime _connectedAt;
        private string _filter = "all";
        private string _search = "";
        private string _selectedCountry;
        private IpInfo _realIp;
        private VpnEngine.State _state = VpnEngine.State.Idle;
        private string _stage = "";
        private System.Windows.Forms.NotifyIcon _tray;
        private bool _realIpLoaded;

        private bool _ipSameWarned;

        private bool _recovering;
        private bool _stageLive;
        private string _autoLastCountry;

        public MainWindow()
        {
            InitializeComponent();

            _settings = Program.Settings;
            _cache = new ServerCache(Program.Paths.ServerCacheFile, Program.Log);
            _book = new ServerBook(Program.Log);
            _killSwitch = new KillSwitch(Program.Log);
            _engine = new VpnEngine(Program.Paths, Program.Log, _killSwitch);
            _ipChecker = new IpChecker(Program.Log);
            _netStats = new NetStats(Program.Log);

            _selectedCountry = _settings.SelectedCountry;

            _engine.StateChanged += s => Dispatcher.BeginInvoke((Action)(() => OnEngineState(s)));
            _engine.StageChanged += t => Dispatcher.BeginInvoke((Action)(() => { _stage = t; RefreshHero(); }));
            _engine.TunnelLost += reason => Dispatcher.BeginInvoke((Action)(() => OnTunnelLost(reason)));
            _engine.LogLine += line => Dispatcher.BeginInvoke((Action)(() => AppendLog(line)));

            Loc.Changed += () =>
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    ApplyLanguage();
                    RenderCountries();
                    BuildSettings();
                    UpdateSelectedCountryLabel();
                }));
            };

            _uiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            Loc.Lang = _settings.Language ?? Loc.DetectDefault();
            ApplyLanguage();
            WireTooltips();
            InitTray();
            SetState(VpnEngine.State.Idle);
            ShowAdminIfNeeded();
        }

        // ===================================================================
        // startup
        // ===================================================================

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Fit small laptop / VM screens: never taller than the work area.
            var wa = SystemParameters.WorkArea;
            if (Height > wa.Height - 24)
                Height = Math.Max(MinHeight, wa.Height - 24);
            if (Width > wa.Width - 24)
                Width = Math.Max(MinWidth, wa.Width - 24);
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);

            RootShell.Opacity = 0;
            await Task.Delay(30);
            Theme.FadeIn(RootShell, 260);

            // The bootstrap must start here: only now is a Dispatcher
            // SynchronizationContext installed, so every await below resumes
            // on the UI thread instead of the thread pool.
            await BootstrapAsync();
        }

        private async Task BootstrapAsync()
        {
            // 1. cached list so the picker is instant on the next launch
            var cached = _cache.Load();
            if (!string.IsNullOrEmpty(cached))
            {
                var servers = ParseCache(cached);
                if (servers.Count > 0)
                {
                    _book.SetServers(servers, _settings.Favorites.Keys);
                    RenderCountries();
                }
            }

            // 2. public IP of the real connection (in parallel with the list)
            var ipTask = LoadRealIpAsync();

            // 3. fresh list from the network
            await RefreshServersAsync(silent: _book.Servers.Count > 0);
            try { await ipTask; } catch { }

            // 4. auto-connect
            if (_settings.AutoConnect && _book.Servers.Count > 0 && !_busy)
            {
                var target = PickForCountry(_settings.SelectedCountry) ?? _book.SmartPick();
                if (target != null) await ConnectAsync(target, _settings.SelectedCountry);
            }
        }

        private async Task LoadRealIpAsync()
        {
            try
            {
                var info = await _ipChecker.GetAsync(CancellationToken.None);
                if (info != null && info.Ok)
                {
                    _realIp = info;
                    _realIpLoaded = true;
                    RealIpText.Text = info.Ip;
                }
                else
                {
                    RealIpText.Text = Loc.T("ip.unknown");
                }
            }
            catch { RealIpText.Text = Loc.T("ip.unknown"); }
        }

        private async Task RefreshServersAsync(bool silent = false)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                SkeletonPanel.Visibility = Visibility.Visible;
                if (_book.Servers.Count > 0) EmptyState.Visibility = Visibility.Collapsed;
                SetRefreshButtonState(true);

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    var gate = Task.Run(() => ServerSource.LoadAsync(Program.Log, cts.Token));
                    var bookTask = Task.Run(() => VpnBookSource.LoadAsync(Program.Log, cts.Token));
                    var servers = await gate;
                    VpnBookSource.Bundle vb = null;
                    try
                    {
                        if (await Task.WhenAny(bookTask, Task.Delay(12000)) == bookTask)
                            vb = bookTask.Result;
                    }
                    catch (Exception ex) { Program.Log.Warn("vpnbook merge failed: " + ex.Message); }
                    if (vb != null && vb.Ok)
                    {
                        var seen = new HashSet<string>(servers.Select(x => x.CountryCode + "|" + x.Host));
                        foreach (var extra in vb.Servers)
                            if (seen.Add(extra.CountryCode + "|" + extra.Host)) servers.Add(extra);
                        Program.Log.Info("merged providers: " + servers.Count + " servers");
                    }
                    if (servers.Count > 0)
                    {
                        _book.SetServers(servers, _settings.Favorites.Keys);
                        EnsureDefaultCountry();
                        _cache.Save(BuildCacheJson(servers));
                        RenderCountries();
                        if (!silent)
                            Toast(Loc.T("toast.servers", servers.Count), ToastKind.Ok);
                        LastUpdatedText.Text = Loc.T("picker.updated", DateTime.Now.ToString("HH:mm"));
                        _ = MeasureLatencyAsync();
                    }
                    else if (_book.Servers.Count == 0)
                    {
                        ShowEmptyServers();
                        if (!silent) Toast(Loc.T("toast.noservers"), ToastKind.Err);
                    }
                }
            }
            finally
            {
                SkeletonPanel.Visibility = Visibility.Collapsed;
                _busy = false;
                SetRefreshButtonState(false);
                UpdateServerBadge();
            }
        }

        /// <summary>
        /// Fresh install / empty selection: preselect the fastest place so the
        /// big button is actionable instead of nagging for a country.
        /// </summary>
        private void EnsureDefaultCountry()
        {
            if (!string.IsNullOrEmpty(_selectedCountry) && _book.GetCountry(_selectedCountry) != null)
            {
                RefreshHero();
                return;
            }
            var best = _book.SmartPick();
            if (best == null)
            {
                var first = _book.Countries.Values.FirstOrDefault();
                if (first == null) return;
                best = first.Best();
            }
            if (best == null) return;
            _selectedCountry = best.CountryCode;
            _settings.SelectedCountry = _selectedCountry;
            _settings.Save(Program.Paths.SettingsFile);
            HighlightByCode(_selectedCountry);
            Program.Log.Info("default country: " + _selectedCountry);
            UpdateSelectedCountryLabel();
            RefreshHero();
        }

        private void HighlightByCode(string code)
        {
            foreach (var child in CountryList.Children)
            {
                var border = child as Border;
                if (border == null) continue;
                bool on = string.Equals(border.Tag as string, code, StringComparison.OrdinalIgnoreCase);
                border.Background = on ? Theme.Solid(Color.FromArgb(0x2E, 0x3B, 0x82, 0xF6)) : Brushes.Transparent;
                border.BorderBrush = on ? Theme.Solid(Color.FromArgb(0x4D, 0x78, 0xAA, 0xFF)) : Brushes.Transparent;
            }
        }

        private async Task MeasureLatencyAsync()
        {
            try
            {
                var progress = new Progress<int>(p => { if (p >= 100) RenderCountries(); });
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                {
                    await _book.MeasureLatencyAsync(progress, cts.Token);
                }
                RenderCountries();
                Program.Log.Info("latency probe finished");
            }
            catch (Exception ex) { Program.Log.Warn("latency probe: " + ex.Message); }
        }

        private void ShowEmptyServers()
        {
            Program.Log.Warn("show empty servers state");
            EmptyState.Visibility = Visibility.Visible;
            EmptyTitle.Text = Loc.T("picker.noservers");
            EmptyHint.Text = Loc.T("picker.noservers.hint");
            LastUpdatedText.Text = Loc.T("picker.never");
        }

        private void SetRefreshButtonState(bool loading)
        {
            BtnRefreshList.IsEnabled = !loading;
            BtnRefreshList.Content = loading ? Loc.T("picker.loading") : Loc.T("picker.refresh");
        }

        /// <summary>Reads our own compact cache format (not the VPN Gate CSV).</summary>
        private static List<VpnServer> ParseCache(string json)
        {
            var list = new List<VpnServer>();
            if (string.IsNullOrEmpty(json)) return list;
            int i = 0;
            while (true)
            {
                int start = json.IndexOf('{', i);
                if (start < 0) break;
                int end = json.IndexOf('}', start);
                if (end < 0) break;
                string obj = json.Substring(start, end - start + 1);
                i = end + 1;
                var s = new VpnServer
                {
                    Host = CacheStr(obj, "h"),
                    Ip = CacheStr(obj, "i"),
                    Ping = CacheInt(obj, "p"),
                    SpeedBps = CacheLong(obj, "sp"),
                    Score = CacheLong(obj, "s"),
                    Sessions = CacheInt(obj, "n"),
                    Country = CacheStr(obj, "c"),
                    CountryCode = CacheStr(obj, "cc"),
                    Operator = CacheStr(obj, "o"),
                    ConfigBase64 = CacheStr(obj, "b"),
                    Provider = CacheStr(obj, "pr") ?? "vpngate",
                    AuthUser = CacheStr(obj, "au"),
                    AuthPass = CacheStr(obj, "ap"),
                };
                if (!string.IsNullOrEmpty(s.Host) && !string.IsNullOrEmpty(s.ConfigBase64) && s.CountryCode != null)
                    list.Add(s);
            }
            return list;
        }

        private static string CacheStr(string obj, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(obj, "\"" + key + "\":\"((?:[^\"\\\\]|\\\\.)*)\"" );
            return m.Success ? System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value) : null;
        }

        private static int CacheInt(string obj, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(obj, "\"" + key + "\":(-?\\d+)");
            int v;
            return m.Success && int.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }

        private static long CacheLong(string obj, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(obj, "\"" + key + "\":(-?\\d+)");
            long v;
            return m.Success && long.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }

        private string BuildCacheJson(List<VpnServer> servers)
        {
            var sb = new System.Text.StringBuilder(servers.Count * 2200);
            sb.Append("[");
            for (int i = 0; i < servers.Count; i++)
            {
                var s = servers[i];
                if (i > 0) sb.Append(",");
                sb.Append("{\"h\":\"").Append(Security.JsonEscape(s.Host))
                  .Append("\",\"i\":\"").Append(Security.JsonEscape(s.Ip))
                  .Append("\",\"p\":").Append(s.Ping)
                  .Append(",\"sp\":").Append(s.SpeedBps)
                  .Append(",\"s\":").Append(s.Score)
                  .Append(",\"n\":").Append(s.Sessions)
                  .Append(",\"c\":\"").Append(Security.JsonEscape(s.Country))
                  .Append("\",\"cc\":\"").Append(Security.JsonEscape(s.CountryCode))
                  .Append("\",\"o\":\"").Append(Security.JsonEscape(s.Operator ?? ""))
                  .Append("\",\"b\":\"").Append(Security.JsonEscape(s.ConfigBase64))
                  .Append("\",\"pr\":\"").Append(Security.JsonEscape(s.Provider ?? "vpngate"))
                  .Append("\",\"au\":\"").Append(Security.JsonEscape(s.AuthUser ?? ""))
                  .Append("\",\"ap\":\"").Append(Security.JsonEscape(s.AuthPass ?? ""))
                  .Append("\"}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        // ===================================================================
        // engine state -> UI
        // ===================================================================

        private void OnEngineState(VpnEngine.State state)
        {
            SetState(state);
            if (state == VpnEngine.State.Connected)
            {
                _connectedAt = DateTime.Now;
                _ipSameWarned = false;
                _netStats.Reset();
                var srv = _engine.ActiveServer;
                var ip = _engine.ActiveIp;
                VpnIpText.Text = ip != null && ip.Ok ? ip.Ip : "—";
                if (srv != null)
                {
                    UpdateSelectedCountryLabel();
                    StatusLocFlag.Source = Flags.Get(srv.CountryCode);
                    StatusLocName.Text = Loc.Country(srv.CountryCode, srv.Country) + " · " + srv.Host;
                    StatusLocSep.Visibility = Visibility.Visible;
                    StatusLocPanel.Visibility = Visibility.Visible;
                    _settings.SelectedCountry = srv.CountryCode;
                    _settings.SelectedServerHost = srv.Host;
                    _settings.Save(Program.Paths.SettingsFile);
                }
                Toast(Loc.T("toast.connected", srv == null ? "" : Loc.Country(srv.CountryCode, srv.Country)), ToastKind.Ok);
                _ = VerifyIpAsync();
            }
            else if (state == VpnEngine.State.Idle)
            {
                StatusLocSep.Visibility = Visibility.Collapsed;
                StatusLocPanel.Visibility = Visibility.Collapsed;
                VpnIpText.Text = "—";
            }
        }

        private void SetState(VpnEngine.State state)
        {
            _state = state;
            var appState = state == VpnEngine.State.Connected ? "connected"
                         : state == VpnEngine.State.Connecting ? "connecting"
                         : state == VpnEngine.State.Disconnecting ? "disconnecting" : "disconnected";
            RootGrid.Tag = appState;
            RefreshHero();
        }

        private void RefreshHero()
        {
            string key;
            Color dotColor;
            Color ringColor;
            switch (_state)
            {
                case VpnEngine.State.Connected:
                    key = "status.connected"; dotColor = Theme.Good; ringColor = Theme.Good; break;
                case VpnEngine.State.Connecting:
                    key = "status.connecting"; dotColor = Theme.Warn; ringColor = Theme.Warn; break;
                case VpnEngine.State.Disconnecting:
                    key = "status.disconnecting"; dotColor = Theme.Warn; ringColor = Theme.TextMute; break;
                default:
                    key = "status.disconnected"; dotColor = Theme.TextMute; ringColor = Theme.Accent; break;
            }
            StatusText.Text = Loc.T(key);
            StatusDot.Fill = Theme.Solid(dotColor);
            PowerRingBorder.Stroke = Theme.Solid(Color.FromArgb(_state == VpnEngine.State.Connected ? (byte)0x8C : (byte)0x3D,
                ringColor.R, ringColor.G, ringColor.B));
            PowerIcon.Stroke = Theme.Solid(_state == VpnEngine.State.Connected ? Theme.Good
                : _state == VpnEngine.State.Connecting ? Theme.Warn : Color.FromRgb(0x7D, 0xB3, 0xFF));
            if (PowerGlowEffect != null)
            {
                PowerGlowEffect.Color = _state == VpnEngine.State.Connected ? Theme.Good : Theme.Accent;
                PowerGlowEffect.Opacity = _state == VpnEngine.State.Connected ? 0.5 : 0.35;
            }

            string hintKey = _state == VpnEngine.State.Connected ? "hero.hint.disconnect"
                : _state == VpnEngine.State.Connecting ? "hero.hint.cancel"
                : _state == VpnEngine.State.Disconnecting ? "hero.hint.busy"
                : string.IsNullOrEmpty(_selectedCountry) ? "hero.hint.nocountry" : "hero.hint.connect";
            PowerHint.Text = (_stageLive && !string.IsNullOrEmpty(_stage) &&
                              (_state == VpnEngine.State.Connecting || _state == VpnEngine.State.Disconnecting))
                ? _stage + Loc.T("hero.hint.cancelsuffix") : Loc.T(hintKey);
            if (_state == VpnEngine.State.Idle || _state == VpnEngine.State.Connected) _stageLive = false;

            Grid.SetRowSpan(ToastHost, 1);
            var glow = TopGlow.Fill as RadialGradientBrush;
            if (glow != null)
            {
                var c = _state == VpnEngine.State.Connected ? Theme.Good
                    : _state == VpnEngine.State.Connecting ? Theme.Warn
                    : Theme.Accent;
                var stops = glow.GradientStops;
                stops[0].Color = Color.FromArgb(_state == VpnEngine.State.Connected ? (byte)0x6E : (byte)0x5A, c.R, c.G, c.B);
                stops[1].Color = Color.FromArgb(0x0A, c.R, c.G, c.B);
                stops[2].Color = Color.FromArgb(0x00, c.R, c.G, c.B);
            }
            if (_state == VpnEngine.State.Connecting || _state == VpnEngine.State.Disconnecting)
                StartPulse();
            else StopPulse();
        }

        private Storyboard _pulse;
        private void StartPulse()
        {
            if (_pulse != null) return;
            var anim = new DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(900))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            _pulse = new Storyboard();
            _pulse.Children.Add(anim);
            Storyboard.SetTarget(anim, PowerRingBorder);
            Storyboard.SetTargetProperty(anim, new PropertyPath("Opacity"));
            _pulse.Begin();
        }

        private void StopPulse()
        {
            if (_pulse == null) return;
            _pulse.Stop();
            _pulse = null;
            PowerRingBorder.Opacity = 1;
        }

        // ===================================================================
        // connect / disconnect
        // ===================================================================

        private async void PowerButton_Click(object sender, RoutedEventArgs e)
        {
            // Escape hatch first: while connecting, the same button aborts the
            // attempt immediately instead of being swallowed by the busy guard.
            if (_state == VpnEngine.State.Connecting)
            {
                AbortConnect();
                return;
            }
            if (_busy) return;
            if (_state == VpnEngine.State.Connected)
            {
                _opCts?.Cancel();
                _engine.Disconnect();
                Toast(Loc.T("toast.disconnected"), ToastKind.Info);
                return;
            }

            string country = _selectedCountry;
            if (string.IsNullOrEmpty(country))
            {
                Toast(Loc.T("toast.selectcountry"), ToastKind.Warn);
                return;
            }
            var server = PickForCountry(country);
            if (server == null)
            {
                Toast(Loc.T("toast.selectcountry"), ToastKind.Warn);
                return;
            }
            await ConnectAsync(server, country);
        }

        /// <summary>
        /// Cancels an in-flight connection attempt: kills openvpn, drops the
        /// arrows/pulse animation and puts the UI back to a usable idle state at
        /// once, without waiting for the engine's process teardown.
        /// </summary>
        private void AbortConnect()
        {
            if (_state != VpnEngine.State.Connecting && !_busy) return;
            Program.Log.Info("user cancelled the connection attempt");
            try { _opCts?.Cancel(); } catch { }
            _stageLive = false;
            SetState(VpnEngine.State.Idle);
            RefreshHero();
            Toast(Loc.T("toast.cancelled"), ToastKind.Info);
            try { _engine.Disconnect(); } catch { }
        }

        private async Task ConnectAsync(VpnServer server, string country)
        {
            if (_busy) return;
            _busy = true;
            _opCts = new CancellationTokenSource();
            _autoLastCountry = country;
            SetState(VpnEngine.State.Connecting);
            StageText(Loc.T("stage.tunnel"));
            try
            {
                var alternatives = OrderServers(country);
                Program.Log.Info("connecting to " + country + " with " + alternatives.Count + " ranked candidates");
                var result = await _engine.ConnectAsync(server, _settings, alternatives, _opCts.Token);
                if (result.Cancelled || _state == VpnEngine.State.Idle)
                {
                    // the user pulled the plug; nothing to report
                    return;
                }
                if (!result.Ok)
                {
                    // A local driver problem fails identically on every server, so
                    // switching providers would only waste another minute.
                    if (result.ErrorKey == "err.adapter" || result.ErrorKey == "err.driver")
                    {
                        SetState(VpnEngine.State.Idle);
                        Toast(Loc.T("toast.failed") + ": " + Loc.T(result.ErrorKey), ToastKind.Err);
                        return;
                    }
                    // Free VPN Gate nodes reject anonymous logins from many IPs.
                    // Before giving up, try the stable VPNBook pool in the same
                    // country (or the nearest one that exists).
                    var fallback = _book.FallbackFor(server, country);
                    if (fallback != null)
                    {
                        Program.Log.Info("fallback to " + fallback.Provider + " " + fallback.Host);
                        StageText(Loc.T("stage.tunnel") + " · " + fallback.Host);
                        SetState(VpnEngine.State.Connecting);
                        result = await _engine.ConnectAsync(fallback, _settings, new List<VpnServer> { fallback }, _opCts.Token);
                    }
                    if (result.Cancelled)
                    {
                        SetState(VpnEngine.State.Idle);
                        return;
                    }
                    if (!result.Ok)
                    {
                        SetState(VpnEngine.State.Idle);
                        Toast(Loc.T("toast.failed") + ": " + Loc.T(result.ErrorKey), ToastKind.Err);
                    }
                }
            }
            finally
            {
                _busy = false;
                PowerButton.IsEnabled = true;
                _opCts = null;
                if (_state == VpnEngine.State.Connecting) SetState(VpnEngine.State.Idle);
                UpdateServerBadge();
            }
        }

        /// <summary>
        /// The tunnel died after being established. Tell the user and try to
        /// re-establish on the same country once, because a volunteer node
        /// restarting its service is common and usually recovers.
        /// </summary>
        private async void OnTunnelLost(string reason)
        {
            if (_state != VpnEngine.State.Connected) return;
            // the watchdog may fire twice while the process is being torn down
            if (_recovering) return;
            _recovering = true;
            Program.Log.Warn("tunnel lost (" + reason + ") - reconnecting");

            SetState(VpnEngine.State.Idle);
            Toast(Loc.T("toast.dropped"), ToastKind.Warn);

            try { _engine.Disconnect(); } catch { }

            string country = _settings.SelectedCountry;
            if (string.IsNullOrEmpty(country)) return;

            await Task.Delay(1200);
            if (_state != VpnEngine.State.Idle || _busy) return;

            var srv = PickForCountry(country);
            try
            {
                if (srv != null) await ConnectAsync(srv, country);
            }
            finally { _recovering = false; }
        }

        private void StageText(string text)
        {
            _stage = text;
            _stageLive = true;
            RefreshHero();
        }

        private VpnServer PickForCountry(string country)
        {
            var c = _book.GetCountry(country);
            if (c == null) return _book.SmartPick();
            return c.Best();
        }

        private List<VpnServer> OrderServers(string country)
        {
            var c = _book.GetCountry(country);
            if (c == null) return new List<VpnServer>();
            // VPN Gate volunteers frequently answer AUTH_FAILED (their public
            // login is rate-limited per source IP), while VPNBook's small pool is
            // stable. So a VPNBook node for the chosen country is tried first;
            // the richer VPN Gate list stays as the wide-coverage fallback.
            return c.Servers
                .OrderBy(x => string.Equals(x.Provider, "vpnbook", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                // A profile that already speaks UDP is worth more than a fast-looking
                // TCP node: TCP-in-TCP stalls on every loss and measured 0.1 Mbit/s.
                .ThenBy(x => ProtoIsUdp(x) ? 0 : 1)
                .ThenBy(x => x._measuredPing > 0 ? x._measuredPing : (x.Ping > 0 ? x.Ping : 8000))
                .ThenByDescending(x => Math.Min(x.SpeedBps, 100000000))
                .ToList();
        }

        /// <summary>True when the profile's own config asks for UDP.</summary>
        private static bool ProtoIsUdp(VpnServer s)
        {
            var cfg = ServerSource.DecodeConfig(s);
            if (string.IsNullOrEmpty(cfg)) return false;
            return cfg.IndexOf("proto udp", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task VerifyIpAsync()
        {
            // Providers can answer from a pre-tunnel cache for a moment; a couple of
            // spaced retries are what actually produce the tunnel address.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (await VerifyIpOnceAsync()) return;
                try { await Task.Delay(1500); } catch { return; }
            }
        }

        /// <summary>One verification round. True when a trustworthy tunnel IP was shown.</summary>
        private async Task<bool> VerifyIpOnceAsync()
        {
            try
            {
                var info = await _ipChecker.GetAsync(CancellationToken.None);
                if (info == null || !info.Ok) return false;

                // The address shown must describe the tunnel, and it must not lie.
                // Right after connecting, providers can still answer with the
                // pre-tunnel address (connection reuse, DNS answer cached from
                // before the route flipped). Printing that over the connected view
                // made a working tunnel look broken - the log showed the user's own
                // ISP address next to "tunnel up".
                if (_realIp != null && _realIp.Ok && info.Ip == _realIp.Ip)
                {
                    if (_state == VpnEngine.State.Connected)
                        VpnIpText.Text = "…";   // not verified yet, do not show a stale value
                    // warn once per connection, not once per retry
                    if (!_ipSameWarned)
                    {
                        _ipSameWarned = true;
                        Toast(Loc.T("toast.ipsame"), ToastKind.Warn);
                    }
                    return false;
                }
                VpnIpText.Text = info.Ip;
                return true;
            }
            catch { return false; }
        }

        // ===================================================================
        // country list rendering
        // ===================================================================

        private void RenderCountries()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke((Action)RenderCountries);
                return;
            }
            try
            {
                RenderCountriesCore();
            }
            catch (Exception ex)
            {
                Program.Log.Error("render failed: " + ex.GetType().Name + ": " + ex.Message + " | " + (ex.StackTrace ?? "").Split('\n')[0]);
            }
        }

        private void RenderCountriesCore()
        {
            Program.Log.Info("render: begin");
            CountryList.Children.Clear();
            if (_book.Servers.Count == 0) { UpdateServerBadge(); return; }

            List<ServerBook.Country> favorites;
            var ranking = _book.Ranking(_filter, _search, out favorites);
            Program.Log.Info("render: ranking " + ranking.Count);
            bool hasFav = _book.Countries.Values.Any(c => c.IsFavorite);
            if (_filter == "favorite" && !hasFav) ranking = new List<ServerBook.Country>();

            CountryList.Children.Add(BuildTabHeader(ranking.Count));
            int rowNo = 0;
            foreach (var c in ranking)
            {
                try
                {
                    CountryList.Children.Add(BuildCountryRow(c));
                }
                catch (Exception ex)
                {
                    Program.Log.Error("row failed for " + c.Code + ": " + ex.GetType().Name + ": " + ex.Message);
                }
                rowNo++;
                if (rowNo % 5 == 0) Program.Log.Info("render: rows " + rowNo + "/" + ranking.Count);
            }
            Program.Log.Info("render: rows built " + CountryList.Children.Count);

            Program.Log.Info("render: servers=" + _book.Servers.Count + " ranking=" + ranking.Count + " filter=" + _filter + " search='" + _search + "'");
            if (ranking.Count > 0) MarkUiReady();
            EmptyState.Visibility = ranking.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (ranking.Count == 0 && _book.Servers.Count > 0)
            {
                EmptyTitle.Text = Loc.T("picker.empty");
                EmptyHint.Text = Loc.T("picker.emptyhint");
            }
            UpdateServerBadge();
        }

        private UIElement BuildTabHeader(int count)
        {
            var sp = new StackPanel();
            if (_search.Trim().Length > 0)
            {
                var lbl = Theme.Label(Loc.T("picker.empty") == "Nothing found" ? "Found: " + count : "Найдено: " + count, 10.5, Theme.TextMuteBrush);
                lbl.Margin = new Thickness(2, 0, 0, 4);
                sp.Children.Add(lbl);
            }
            return sp;
        }

        private Border BuildCountryRow(ServerBook.Country c)
        {
            bool selected = string.Equals(c.Code, _selectedCountry, StringComparison.OrdinalIgnoreCase);
            var row = new Border
            {
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(9, 8, 9, 8),
                Margin = new Thickness(0, 0, 0, 1),
                Background = selected ? Theme.Solid(Color.FromArgb(0x2E, 0x3B, 0x82, 0xF6)) : Brushes.Transparent,
                BorderBrush = selected ? Theme.Solid(Color.FromArgb(0x4D, 0x78, 0xAA, 0xFF)) : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = c.Code,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var flag = new Image
            {
                Source = Flags.Get(c.Code),
                Width = 28,
                Height = 21,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            flag.Clip = new RectangleGeometry(new Rect(0, 0, 28, 21), 2.5, 2.5);
            Grid.SetColumn(flag, 0);
            grid.Children.Add(flag);

            var name = Theme.Label(Loc.Country(c.Code, c.Name), 13, Theme.TextBrush, FontWeights.SemiBold);
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            int n = c.Servers.Count;
            sub.Children.Add(Theme.Label(n == 1 ? Loc.T("row.server.one") : Loc.T("row.server.many", n), 10, Theme.TextMuteBrush));
            if (c.BestPing > 0 && c.BestPing < 2000)
            {
                sub.Children.Add(new Border
                {
                    Width = 3, Height = 3, CornerRadius = new CornerRadius(2), Background = Theme.Solid(Theme.TextMute),
                    Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
                });
                sub.Children.Add(Theme.Label(Loc.T("row.bestping", c.BestPing), 10, Theme.TextMuteBrush));
            }
            var main = new StackPanel();
            main.Children.Add(name);
            main.Children.Add(sub);
            Grid.SetColumn(main, 1);
            grid.Children.Add(main);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            // load bars (signal strength)
            right.Children.Add(BuildLoadBars(c));
            if (c.IsFavorite)
            {
                right.Children.Add(Theme.Icon("M12 3.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8L3.5 9.7l5.9-.9L12 3.5Z", 11, Theme.WarnBrush));
            }
            right.Children.Add(new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(8),
            });
            // chevron
            var chev = Theme.StrokeIcon("M9 6l6 6-6 6", 12, Theme.Solid(Color.FromArgb(0x66, 0x93, 0xA0, 0xBD)));
            chev.Margin = new Thickness(2, 0, 2, 0);
            right.Children.Add(chev);

            // favorite star hit area
            var star = MakeFavButton(c);
            right.Children.Add(star);

            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            row.Child = grid;
            row.MouseLeftButtonUp += (s, e) => SelectCountry(c);
            row.MouseEnter += (s, e) =>
            {
                if (!selected) row.Background = Theme.HoverBrush;
            };
            row.MouseLeave += (s, e) =>
            {
                if (!selected) row.Background = Brushes.Transparent;
            };
            return row;
        }

        private Button MakeFavButton(ServerBook.Country c)
        {
            var b = new Button
            {
                Width = 22,
                Height = 22,
                Style = (Style)FindResource("TinyIconButton"),
                ToolTip = Loc.T("tabs.favorite"),
                Content = Theme.Icon("M12 3.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8L3.5 9.7l5.9-.9L12 3.5Z", 12,
                    c.IsFavorite ? Theme.WarnBrush : Theme.Solid(Color.FromArgb(0x59, 0x93, 0xA0, 0xBD))),
            };
            b.Click += (s, e) =>
            {
                e.Handled = true;
                c.IsFavorite = !c.IsFavorite;
                if (c.IsFavorite) _settings.Favorites[c.Code.ToUpperInvariant()] = 1;
                else _settings.Favorites.Remove(c.Code.ToUpperInvariant());
                _settings.Save(Program.Paths.SettingsFile);
                RenderCountries();
            };
            return b;
        }

        private UIElement BuildLoadBars(ServerBook.Country c)
        {
            int level = LoadLevel(c);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Height = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
            sp.RenderTransformOrigin = new Point(0.5, 0.5);
            for (int i = 0; i < 3; i++)
            {
                sp.Children.Add(new Border
                {
                    Width = 3,
                    Height = 5 + i * 3.5,
                    CornerRadius = new CornerRadius(1.5),
                    Background = i < level ? Theme.Solid(Color.FromRgb(0x5E, 0xEA, 0xD4)) : Theme.Solid(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(0, i == 0 ? 0 : 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Bottom,
                });
            }
            return sp;
        }

        private static int LoadLevel(ServerBook.Country c)
        {
            int ping = c.BestPing;
            if (ping <= 0) return 1;
            if (ping < 90) return 3;
            if (ping < 220) return 2;
            return 1;
        }

        private void MarkUiReady()
        {
            try
            {
                if (System.IO.Directory.Exists(Program.Paths.DataDir))
                    System.IO.File.WriteAllText(System.IO.Path.Combine(Program.Paths.DataDir, "ui-ready.txt"), DateTime.Now.ToString("O"));
            }
            catch { }
        }

        private void UpdateServerBadge()
        {
            ServerCountText.Text = _book.Servers.Count.ToString();
            if (_book.Servers.Count == 0 && !_busy)
                EmptyState.Visibility = Visibility.Visible;
        }

        private void UpdateSelectedCountryLabel()
        {
            if (string.IsNullOrEmpty(_selectedCountry)) return;
            var c = _book.GetCountry(_selectedCountry);
            if (c != null)
            {
                StatusLocFlag.Source = Flags.Get(c.Code);
            }
        }

        private void SelectCountry(ServerBook.Country c)
        {
            _selectedCountry = c.Code;
            _settings.SelectedCountry = c.Code;
            _settings.SelectedServerHost = null;
            _settings.Save(Program.Paths.SettingsFile);
            RenderCountries();
            RefreshHero();
            if (_state == VpnEngine.State.Connected)
            {
                // switching country while connected reconnects through the new exit
                var server = c.Best();
                if (server != null) _ = ReconnectAsync(server, c.Code);
            }
        }

        private async Task ReconnectAsync(VpnServer server, string country)
        {
            if (_busy) return;
            _engine.Disconnect();
            await Task.Delay(250);
            await ConnectAsync(server, country);
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            _filter = (string)btn.Tag;
            foreach (var b in new[] { TabAll, TabFast, TabFavorite, TabNearby })
                b.Tag = b == btn ? "active" : b.Tag.ToString() == "active" ? (object)b.Tag : b.Tag;
            // normalize: keep logical ids in a separate map
            TabAll.Tag = btn == TabAll ? "active" : "all";
            TabFast.Tag = btn == TabFast ? "active" : "fast";
            TabFavorite.Tag = btn == TabFavorite ? "active" : "favorite";
            TabNearby.Tag = btn == TabNearby ? "active" : "nearby";
            RenderCountries();
        }

        private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            _search = SearchInput.Text ?? "";
            SearchPlaceholder.Visibility = _search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            SearchClearBtn.Visibility = _search.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            RenderCountries();
        }

        private void SearchClearBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchInput.Text = "";
            SearchInput.Focus();
        }

        private async void BtnSmart_Click(object sender, RoutedEventArgs e)
        {
            if (_book.Servers.Count == 0)
            {
                Toast(Loc.T("toast.noservers"), ToastKind.Warn);
                return;
            }
            var best = _book.SmartPick();
            if (best == null) return;
            _selectedCountry = best.CountryCode;
            _settings.SelectedCountry = best.CountryCode;
            _settings.Save(Program.Paths.SettingsFile);
            RenderCountries();
            Toast(Loc.T("toast.smart", Loc.Country(best.CountryCode, best.Country)), ToastKind.Ok);
            if (_state == VpnEngine.State.Connected) await ReconnectAsync(best, best.CountryCode);
        }

        private async void BtnRefreshList_Click(object sender, RoutedEventArgs e)
        {
            await RefreshServersAsync();
            if (!_realIpLoaded) await LoadRealIpAsync();
        }

        private async void BtnRefreshIp_Click(object sender, RoutedEventArgs e)
        {
            BtnRefreshIp.Content = "…";
            await LoadRealIpAsync();
            if (_state == VpnEngine.State.Connected) await VerifyIpAsync();
            BtnRefreshIp.Content = Loc.T("ip.refresh");
            Toast(Loc.T("toast.ipchanged", RealIpText.Text), ToastKind.Info);
        }

        // ===================================================================
        // timers
        // ===================================================================

        private void UiTimer_Tick(object sender, EventArgs e)
        {
            if (_state == VpnEngine.State.Connected)
            {
                _netStats.Sample();
                DownValue.Text = _netStats.DownMbps.ToString("0.0");
                UpValue.Text = _netStats.UpMbps.ToString("0.0");
                var span = DateTime.Now - _connectedAt;
                TimeValue.Text = span.TotalHours >= 1
                    ? ((int)span.TotalHours).ToString("00") + ":" + span.Minutes.ToString("00")
                    : span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00");
                TrafficValue.Text = FormatBytes(_netStats.TotalBytes);
            }
            else if (_state == VpnEngine.State.Idle || _state == VpnEngine.State.Disconnecting)
            {
                DownValue.Text = "0.0";
                UpValue.Text = "0.0";
                TimeValue.Text = "00:00";
                if (_state == VpnEngine.State.Idle && _engine.ActiveServer == null) TrafficValue.Text = "0";
            }
        }

        private string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024 / 1024).ToString("0.00") + " " + Loc.T("unit.gb");
            return (bytes / 1024.0 / 1024).ToString("0.00") + " " + Loc.T("unit.mb");
        }

        // ===================================================================
        // language
        // ===================================================================

        private void ApplyLanguage()
        {
            TaglineText.Text = Loc.T("app.tag");
            RealIpLabel.Text = Loc.T("ip.real").ToUpperInvariant();
            VpnIpLabel.Text = Loc.T("ip.vpn").ToUpperInvariant();
            BtnRefreshIp.Content = Loc.T("ip.refresh");
            DownLabel.Text = Loc.T("unit.mbps") + " ↓";
            UpLabel.Text = Loc.T("unit.mbps") + " ↑";
            TimeLabel.Text = Loc.T("metrics.session");
            PickerTitle.Text = Loc.T("picker.title");
            SmartText.Text = Loc.T("picker.smart");
            SearchPlaceholder.Text = Loc.T("picker.search");
            EmptyTitle.Text = Loc.T("picker.empty");
            EmptyHint.Text = Loc.T("picker.emptyhint");
            LastUpdatedText.Text = _book.LastUpdate == DateTime.MinValue ? Loc.T("picker.never") : Loc.T("picker.updated", _book.LastUpdate.ToString("HH:mm"));
            TabAll.Content = Loc.T("tabs.all");
            TabFast.Content = Loc.T("tabs.fast");
            TabFavorite.Content = Loc.T("tabs.favorite");
            TabNearby.Content = Loc.T("tabs.nearby");
            SettingsTitle.Text = Loc.T("settings.title");
            LogTitle.Text = Loc.T("logs.title");
            BtnCopyLog.Content = Loc.T("logs.copy");
            AdminTitle.Text = Loc.T("admin.title");
            AdminText.Text = Loc.T("admin.text");
            BtnRestartAdmin.Content = Loc.T("admin.restart");
            BtnAdminLater.Content = Loc.T("admin.later");
            BtnRu.Tag = Loc.Lang == "ru" ? "active" : null;
            BtnEn.Tag = Loc.Lang == "en" ? "active" : null;
            BtnSettings.ToolTip = Loc.T("settings.title");
            BtnMinimize.ToolTip = Loc.T("tray.minimized");
            BtnClose.ToolTip = Loc.T("ctx.close");
            RefreshHero();
            if (_realIpLoaded && _realIp != null) RealIpText.Text = _realIp.Ip;
            else RealIpText.Text = Loc.T("ip.unknown");
        }

        private void BtnRu_Click(object sender, RoutedEventArgs e) => SwitchLanguage("ru");
        private void BtnEn_Click(object sender, RoutedEventArgs e) => SwitchLanguage("en");

        private void SwitchLanguage(string lang)
        {
            Loc.Lang = lang;
            _settings.Language = lang;
            _settings.Save(Program.Paths.SettingsFile);
        }

        // ===================================================================
        // settings sheet
        // ===================================================================

        private void BuildSettings()
        {
            SettingsBody.Children.Clear();

            SettingsBody.Children.Add(SectionLabel(Loc.T("settings.language")));
            var langSwitch = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = Theme.Solid(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
                BorderBrush = Theme.LineBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                Margin = new Thickness(0, 0, 0, 16),
            };
            var langGrid = new UniformGrid { Columns = 2 };
            langGrid.Children.Add(SettingsSegButton("Русский", Loc.Lang == "ru", () => SwitchLanguage("ru")));
            langGrid.Children.Add(SettingsSegButton("English", Loc.Lang == "en", () => SwitchLanguage("en")));
            langSwitch.Child = langGrid;
            SettingsBody.Children.Add(langSwitch);

            SettingsBody.Children.Add(ToggleRow(Loc.T("settings.killswitch"), Loc.T("settings.killswitch.hint"), _settings.KillSwitch, v =>
            {
                _settings.KillSwitch = v;
                _settings.Save(Program.Paths.SettingsFile);
                Toast(Loc.T(v ? "toast.killswitch.on" : "toast.killswitch.off"), ToastKind.Info);
            }));
            SettingsBody.Children.Add(ToggleRow(Loc.T("settings.autoconnect"), Loc.T("settings.autoconnect.hint"), _settings.AutoConnect, v =>
            {
                _settings.AutoConnect = v;
                _settings.Save(Program.Paths.SettingsFile);
            }));
            SettingsBody.Children.Add(ToggleRow(Loc.T("settings.launch"), Loc.T("settings.launch.hint"), _settings.LaunchWithWindows, v =>
            {
                _settings.LaunchWithWindows = v;
                SetStartup(v);
                _settings.Save(Program.Paths.SettingsFile);
            }));

            SettingsBody.Children.Add(SectionLabel(Loc.T("settings.transport")));
            var protoBorder = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = Theme.Solid(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
                BorderBrush = Theme.LineBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                Margin = new Thickness(0, 0, 0, 6),
            };
            var protoGrid = new UniformGrid { Columns = 3 };
            protoGrid.Children.Add(SettingsSegButton(Loc.T("settings.transport.auto"), _settings.Transport == "auto", () => SetTransport("auto")));
            protoGrid.Children.Add(SettingsSegButton(Loc.T("settings.transport.tcp"), _settings.Transport == "tcp", () => SetTransport("tcp")));
            protoGrid.Children.Add(SettingsSegButton(Loc.T("settings.transport.udp"), _settings.Transport == "udp", () => SetTransport("udp")));
            protoBorder.Child = protoGrid;
            SettingsBody.Children.Add(protoBorder);
            var protoHint = Theme.Label(Loc.T("settings.transport.hint"), 10.5, Theme.TextMuteBrush);
            protoHint.TextWrapping = TextWrapping.Wrap;
            protoHint.Margin = new Thickness(2, 0, 0, 16);
            SettingsBody.Children.Add(protoHint);

            SettingsBody.Children.Add(SectionLabel(Loc.T("settings.servers")));
            SettingsBody.Children.Add(KvRow(Loc.T("settings.source"), "VPN Gate · VPNBook"));
            SettingsBody.Children.Add(KvRow(Loc.T("settings.engine"), "OpenVPN 2.6 + TAP-Windows6"));
            SettingsBody.Children.Add(KvRow(Loc.T("settings.version"), Program.Version));
            var spacer = new Border { Height = 16 };
            SettingsBody.Children.Add(spacer);

            var danger = new Button
            {
                Content = Loc.T("settings.removeDriver"),
                Style = (Style)FindResource("DangerButton"),
                Margin = new Thickness(0, 0, 0, 6),
            };
            danger.Click += (s, e) =>
            {
                var res = MessageBox.Show(this, Loc.T("settings.removeDriver.hint"), "EndorVPN", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (res == MessageBoxResult.OK)
                {
                    _engine.Disconnect();
                    Program.ProcessTapRemove();
                }
            };
            SettingsBody.Children.Add(danger);

            var logButton = new Button
            {
                Content = Loc.T("logs.title"),
                Style = (Style)FindResource("GhostButton"),
                Margin = new Thickness(0, 4, 0, 0),
            };
            logButton.Click += (s, e) => { LogDrawer.Visibility = Visibility.Visible; SettingsOverlay.Visibility = Visibility.Collapsed; };
            SettingsBody.Children.Add(logButton);
        }

        private void SetTransport(string value)
        {
            _settings.Transport = value;
            _settings.Save(Program.Paths.SettingsFile);
            BuildSettings();
        }

        private TextBlock SectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text.ToUpperInvariant(),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextMuteBrush,
                Margin = new Thickness(2, 0, 0, 7),
            };
        }

        private Button SettingsSegButton(string text, bool active, Action onClick)
        {
            var b = new Button
            {
                Content = text,
                Style = (Style)FindResource("SegButton"),
                Width = double.NaN,
                Height = 30,
                Tag = active ? "active" : null,
                Margin = new Thickness(1),
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        private Border ToggleRow(string title, string hint, bool value, Action<bool> onChange)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush });
            var hintBlock = new TextBlock { Text = hint, FontSize = 10.5, Foreground = Theme.TextMuteBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            text.Children.Add(hintBlock);
            grid.Children.Add(text);

            var toggle = new CheckBox
            {
                Style = (Style)FindResource("SwitchToggle"),
                IsChecked = value,
                VerticalAlignment = VerticalAlignment.Center,
            };
            toggle.Click += (s, e) => onChange(toggle.IsChecked == true);
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);

            return new Border
            {
                Child = grid,
                Padding = new Thickness(0, 10, 0, 10),
                BorderBrush = Theme.Solid(Color.FromArgb(0x14, 0x1B, 0x25, 0x40)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 0, 0, 2),
            };
        }

        private Border KvRow(string key, string value)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var k = Theme.Label(key, 12, Theme.TextDimBrush);
            var v = Theme.Label(value, 12, Theme.TextBrush, FontWeights.SemiBold);
            v.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(v, 1);
            grid.Children.Add(k);
            grid.Children.Add(v);
            return new Border
            {
                Child = grid,
                Padding = new Thickness(2, 6, 2, 6),
                BorderBrush = Theme.Solid(Color.FromArgb(0x14, 0x1B, 0x25, 0x40)),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            BuildSettings();
            SettingsOverlay.Visibility = Visibility.Visible;
            SettingsSlide.X = 26;
            SettingsSheet.Opacity = 0.4;
            Theme.FadeIn(SettingsSheet, 200, 0.4);
            var anim = new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            SettingsSlide.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void SettingsBackdrop_Click(object sender, MouseButtonEventArgs e)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnCloseLog_Click(object sender, RoutedEventArgs e)
        {
            LogDrawer.Visibility = Visibility.Collapsed;
        }

        private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(LogText.Text);
                Toast(Loc.T("logs.copied"), ToastKind.Ok);
            }
            catch { }
        }

        private void AppendLog(string line)
        {
            if (LogDrawer.Visibility != Visibility.Visible) return;
            LogText.AppendText(line + Environment.NewLine);
            LogScroll.ScrollToEnd();
        }

        // ===================================================================
        // window chrome
        // ===================================================================

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            try { DragMove(); } catch { }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (_minimizeToTray) { Hide(); Toast(Loc.T("tray.minimized"), ToastKind.Info); return; }
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_state == VpnEngine.State.Connected)
            {
                var res = MessageBox.Show(this,
                    Loc.Lang == "ru" ? "VPN сейчас подключён. Отключиться и закрыть EndorVPN?" : "VPN is connected. Disconnect and close EndorVPN?",
                    "EndorVPN", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;
            }
            Close();
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (SettingsOverlay.Visibility == Visibility.Visible) SettingsOverlay.Visibility = Visibility.Collapsed;
                else if (LogDrawer.Visibility == Visibility.Visible) LogDrawer.Visibility = Visibility.Collapsed;
                else if (AdminOverlay.Visibility == Visibility.Visible) AdminOverlay.Visibility = Visibility.Collapsed;
                else if (_engine.CurrentState == VpnEngine.State.Connected) PowerButton_Click(null, null);
            }
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                SearchInput.Focus();
                e.Handled = true;
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_closing) return;
            _closing = true;
            _busy = true;
            try { _opCts?.Cancel(); } catch { }
            try { _engine.Disconnect(); } catch { }
            try { _killSwitch.ResetAll(); } catch { }
            try { _tray?.Dispose(); } catch { }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            try { Program.Instance?.Dispose(); } catch { }
        }

        // ===================================================================
        // tray / misc
        // ===================================================================

        private void InitTray()
        {
            try
            {
                _tray = new System.Windows.Forms.NotifyIcon();
                var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("EndorVPN.app.ico");
                _tray.Icon = stream != null ? new System.Drawing.Icon(stream) : System.Drawing.SystemIcons.Shield;
                _tray.Visible = true;
                _tray.Text = "EndorVPN";
                _tray.MouseClick += (s, e) => { Show(); WindowState = WindowState.Normal; Activate(); };
            }
            catch { }
        }

        private void WireTooltips()
        {
            SearchBox.MouseLeftButtonDown += (s, e) => SearchInput.Focus();
            BtnRefreshList.ToolTip = null;
        }

        private void SetStartup(bool enable)
        {
            try
            {
                object runKey = null;
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (enable)
                        key.SetValue("EndorVPN", "\"" + System.Reflection.Assembly.GetEntryAssembly().Location + "\" --minimized");
                    else
                        key.DeleteValue("EndorVPN", false);
                }
            }
            catch (Exception ex)
            {
                Program.Log.Warn("startup toggle failed: " + ex.Message);
            }
        }

        private void ShowAdminIfNeeded()
        {
            if (Security.IsAdministrator()) return;
            AdminOverlay.Visibility = Visibility.Visible;
        }

        private void BtnRestartAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (Security.RestartElevated())
            {
                _closing = true;
                Application.Current.Shutdown();
            }
            else
            {
                Toast(Loc.T("toast.admin"), ToastKind.Err);
            }
        }

        private void BtnAdminLater_Click(object sender, RoutedEventArgs e)
        {
            AdminOverlay.Visibility = Visibility.Collapsed;
        }

        // ===================================================================
        // toasts
        // ===================================================================

        private enum ToastKind { Info, Ok, Warn, Err }

        private void Toast(string text, ToastKind kind)
        {
            if (string.IsNullOrEmpty(text)) return;
            Color accent;
            string glyph;
            switch (kind)
            {
                case ToastKind.Ok: accent = Theme.Good; glyph = "✓"; break;
                case ToastKind.Warn: accent = Theme.Warn; glyph = "!"; break;
                case ToastKind.Err: accent = Theme.Bad; glyph = "✕"; break;
                default: accent = Theme.Accent; glyph = "i"; break;
            }
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            inner.Children.Add(new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
                Background = Theme.Solid(Color.FromArgb(0x33, accent.R, accent.G, accent.B)),
                Child = new TextBlock
                {
                    Text = glyph, FontSize = 11, FontWeight = FontWeights.Bold,
                    Foreground = Theme.Solid(accent),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                VerticalAlignment = VerticalAlignment.Top,
            });
            inner.Children.Add(new TextBlock
            {
                Text = text, FontSize = 12, Foreground = Theme.TextBrush, TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280, Margin = new Thickness(9, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            });
            var toast = new Border
            {
                Child = inner,
                CornerRadius = new CornerRadius(11),
                Background = Theme.Solid(Color.FromArgb(0xF2, 0x10, 0x18, 0x2A)),
                BorderBrush = Theme.Solid(Color.FromArgb(0x59, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(11, 9, 13, 9),
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Effect = Theme.Glow(Colors.Black, 26, 0.35),
            };
            ToastHost.Children.Add(toast);
            if (ToastHost.Children.Count > 3) ToastHost.Children.RemoveAt(0);
            var slide = new TranslateTransform(0, 8);
            toast.RenderTransform = slide;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
            toast.BeginAnimation(OpacityProperty, fade);
            var up = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            slide.BeginAnimation(TranslateTransform.YProperty, up);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == ToastKind.Err ? 5.5 : 3.2) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var outAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                outAnim.Completed += (s2, e2) => ToastHost.Children.Remove(toast);
                toast.BeginAnimation(OpacityProperty, outAnim);
            };
            timer.Start();
        }
    }
}
