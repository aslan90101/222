using System;
using System.IO;
using System.Linq;
using System.Text;
using EndorVPN.Core;

namespace EndorVPN
{
    /// <summary>
    /// Headless self-check for CI: validates the parts that do not need a live
    /// tunnel - server parsing, ranking, config building, encryption at rest,
    /// driver presence, embedded flags and the localization tables.
    /// Exit code 0 = all good.
    /// </summary>
    public static class SmokeTest
    {
        public static int Run(Paths paths, Log log)
        {
            int failures = 0;
            var report = new StringBuilder();

            void Check(string name, bool ok, string detail = "")
            {
                report.AppendLine((ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : " :: " + detail));
                if (!ok) failures++;
            }

            // ---- localization ----
            foreach (var lang in new[] { "ru", "en" })
            {
                Loc.Lang = lang;
                int missing = 0;
                foreach (var key in new[] { "status.disconnected", "status.connected", "hero.hint.connect", "picker.title",
                    "settings.killswitch", "tray.quit", "toast.connected", "err.timeout" })
                {
                    if (Loc.T(key) == key) missing++;
                }
                Check("i18n." + lang, missing == 0, "missing=" + missing);
                Check("i18n.country." + lang, Loc.Country("RU", "Russia") != "Russia" || lang == "en",
                    Loc.Country("RU", "Russia"));
            }

            // ---- flags ----
            Check("flags.count", Flags.Count >= 250, Flags.Count.ToString());
            Check("flags.ru", Flags.Get("RU") != null);
            Check("flags.jp", Flags.Get("JP") != null);

            // ---- server list ----
            var book = new ServerBook(log);
            var source = new ServerSourceProbe();
            var servers = source.LoadSample(log);
            Check("servers.parse", servers.Count >= 10, servers.Count.ToString());
            Check("servers.countries", servers.Select(s => s.CountryCode).Distinct().Count() > 5);
            book.SetServers(servers, new[] { "US" });
            Check("book.countries", book.Countries.Count > 5, book.Countries.Count.ToString());
            var smart = book.SmartPick();
            Check("book.smartpick", smart != null && !string.IsNullOrEmpty(smart.Host));
            var ranking = book.Ranking("all", "", out _);
            Check("book.ranking", ranking.Count == book.Countries.Count);
            var ru = book.GetCountry("RU");
            if (ru != null) Check("book.rubest", ru.Best() != null);

            // ---- config builder ----
            string cfg = ServerSource.DecodeConfig(servers[0]);
            Check("config.decode", !string.IsNullOrEmpty(cfg) && cfg.Contains("remote "));
            string built = ConfigBuilder.Build(servers[0], cfg, new ConfigBuilder.Options { AdapterName = TapManager.AdapterName });
            Check("config.devnode", built.Contains("dev-node \"EndorVPN\""));
            // We must NOT emit redirect-gateway ourselves: both providers push it in
            // PUSH_REPLY, and a local copy duplicates the option ("redirect-gateway
            // and redirect-private at the same time") and installs the default route
            // twice. The pushed copy is authoritative.
            Check("config.gateway", !built.Contains("redirect-gateway"));
            Check("config.tap", built.Contains("tap-windows6"));
            Check("config.remote443", built.Contains("remote " ) && built.Contains(" 443"));
            Check("config.ca", built.Contains("<ca>"));
            // throughput guards: large socket buffers and MTU-derived mssfix
            Check("config.sndbuf", built.Contains("sndbuf 524288") && built.Contains("rcvbuf 524288"));
            Check("config.mssfix", built.Contains("mssfix "));
            // Country comes from the subdomain prefix so newly published VPNBook
            // gateways work without a release (us16 -> US, de220 -> DE, sg1 -> SG).
            Check("vpnbook.country", VpnBookSource.CountryForTest("us16.vpnbook.com") == "US"
                                  && VpnBookSource.CountryForTest("sg1.vpnbook.com") == "SG"
                                  && VpnBookSource.CountryForTest("uk205.vpnbook.com") == "GB");

            // ---- encrypted cache round trip ----
            var cache = new ServerCache(Path.Combine(paths.DataDir, "smoke.cache"), log);
            string payload = "{\"x\":1,\"text\":\"привет\"}";
            cache.Save(payload);
            string loaded = cache.Load();
            Check("cache.roundtrip", loaded == payload, loaded == null ? "null" : "ok");
            var blob = File.ReadAllBytes(Path.Combine(paths.DataDir, "smoke.cache"));
            Check("cache.encrypted", !Encoding.UTF8.GetString(blob).Contains("привет"));
            Check("cache.magic", Encoding.ASCII.GetString(blob, 0, 5) == "EVPN1");

            // ---- settings ----
            var st = AppSettings.Load(Path.Combine(paths.DataDir, "does-not-exist.json"));
            Check("settings.defaults", st.KillSwitch && st.Transport == "auto");
            st.Language = "en";
            st.SelectedCountry = "JP";
            st.Save(Path.Combine(paths.DataDir, "smoke.json"));
            var st2 = AppSettings.Load(Path.Combine(paths.DataDir, "smoke.json"));
            Check("settings.roundtrip", st2.Language == "en" && st2.SelectedCountry == "JP");

            // ---- engine files ----
            Check("engine.openvpn", File.Exists(Path.Combine(paths.EngineDir, "openvpn.exe")));
            Check("engine.tapctl", File.Exists(Path.Combine(paths.EngineDir, "tapctl.exe")));
            Check("engine.driver", File.Exists(Path.Combine(paths.EngineDir, "drivers", "tap-windows-installer.exe")));
            Check("admin", Security.IsAdministrator());

            // ---- json escape ----
            Check("json.escape", Security.JsonEscape("a\"b\\c\n") == "a\\\"b\\\\c\\n");

            File.WriteAllText(Path.Combine(paths.DataDir, "smoke-report.txt"), report.ToString());
            Console.WriteLine(report.ToString());
            Console.WriteLine(failures == 0 ? "SMOKE OK" : "SMOKE FAIL " + failures);
            return failures == 0 ? 0 : 5;
        }

        /// <summary>Embeds a small but real VPN Gate sample so parsing is covered offline.</summary>
        private sealed class ServerSourceProbe
        {
            public System.Collections.Generic.List<VpnServer> LoadSample(Log log)
            {
                string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-servers.csv");
                if (File.Exists(file))
                {
                    log.Info("smoke: parsing " + file);
                    return ServerSource.Parse(File.ReadAllText(file), log);
                }
                return new System.Collections.Generic.List<VpnServer>();
            }
        }
    }
}
