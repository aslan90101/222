using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EndorVPN.Core;

namespace EndorVPN
{
    /// <summary>
    /// Headless "does the tunnel really work" test used by CI and by support:
    /// connects through a real provider, then verifies the public IP changed.
    /// Writes data/e2e.json so the caller can assert on the outcome.
    /// </summary>
    public static class E2eTest
    {
        public static async Task<int> Run(Paths paths, Log log)
        {
            var killSwitch = new KillSwitch(log);
            var engine = new VpnEngine(paths, log, killSwitch);
            var checker = new IpChecker(log);
            var settings = new AppSettings { KillSwitch = false, Transport = "tcp" };
            string outFile = Path.Combine(paths.DataDir, "e2e.json");
            var sb = new StringBuilder();
            sb.Append("{\"stage\":\"start\"");

            IpInfo before = await checker.GetAsync(CancellationToken.None);
            log.Info("e2e ip before: " + (before == null ? "?" : before.Ip));
            sb.Append(",\"ipBefore\":\"").Append(Security.JsonEscape(before == null ? "" : before.Ip)).Append("\"");

            var countries = new[] { "US", "DE", "GB", "FR", "CA", "JP" };
            List<VpnServer> candidates = new List<VpnServer>();

            var vb = await VpnBookSource.LoadAsync(log, CancellationToken.None);
            if (vb != null && vb.Ok) candidates.AddRange(vb.Servers);

            var gate = await ServerSource.LoadAsync(log, CancellationToken.None);
            foreach (var s in gate.Where(x => countries.Contains(x.CountryCode)).OrderBy(x => x.Ping).ThenByDescending(x => x.SpeedBps).Take(6))
                candidates.Add(s);
            log.Info("e2e candidates: " + candidates.Count);

            bool ok = false;
            IpInfo after = null;
            VpnServer used = null;
            foreach (var srv in candidates)
            {
                log.Info("e2e trying " + srv.Host + " (" + srv.CountryCode + ", " + srv.Provider + ")");
                var res = await engine.ConnectAsync(srv, settings, null, CancellationToken.None);
                if (res.Ok)
                {
                    ok = true;
                    used = srv;
                    after = res.Ip;
                    break;
                }
                engine.Disconnect();
                await Task.Delay(400);
            }

            sb.Append(",\"ipAfter\":\"").Append(Security.JsonEscape(after == null ? "" : after.Ip)).Append("\"");
            sb.Append(",\"server\":\"").Append(Security.JsonEscape(used == null ? "" : used.Host)).Append("\"");
            sb.Append(",\"country\":\"").Append(Security.JsonEscape(used == null ? "" : used.CountryCode)).Append("\"");
            sb.Append(",\"provider\":\"").Append(Security.JsonEscape(used == null ? "" : used.Provider)).Append("\"");
            bool changed = ok && after != null && after.Ok && (before == null || !before.Ok || after.Ip != before.Ip);
            sb.Append(",\"connected\":").Append(ok ? "true" : "false");
            sb.Append(",\"ipChanged\":").Append(changed ? "true" : "false");
            sb.Append(",\"done\":true}");

            try { File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false)); } catch { }
            log.Info("e2e result: " + sb.ToString());

            // leave the tunnel up for the caller's own IP/speed checks, then clean up
            await Task.Delay(3000);
            engine.Disconnect();
            killSwitch.ResetAll();
            return changed ? 0 : 3;
        }
    }
}
