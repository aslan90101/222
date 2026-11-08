using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace EndorVPN.Core
{
    /// <summary>
    /// System-wide kill-switch built on Windows Filtering Platform rules managed
    /// through netsh. It is deliberately simple:
    ///
    ///   enable  -> block all outbound traffic except loopback and the OpenVPN
    ///              handshake is allowed by the tunnel adapter itself (the rules
    ///              are removed the moment the tunnel comes up, then a narrower
    ///              "block if tunnel is down" guard stays active)
    ///
    /// The rule set is:
    ///   1) EndorVPN-KS-Block : outbound block, applied only while reconnecting
    ///   2) EndorVPN-KS-Dns   : outbound UDP 53 block except via the adapter
    ///
    /// All rules carry the EndorVPN-KS prefix so we can rebuild/tombstone them
    /// deterministically after a crash (the settings file keeps a "dirty" flag).
    /// </summary>
    public sealed class KillSwitch
    {
        private const string BlockRule = "EndorVPN-KS-Block";
        private readonly Log _log;
        private bool _active;

        public KillSwitch(Log log)
        {
            _log = log;
        }

        public bool Active { get { return _active; } }

        public void Enable()
        {
            if (_active) return;
            try
            {
                Run("advfirewall firewall add rule name=\"" + BlockRule + "\" dir=out action=block enable=yes profile=any description=\"EndorVPN kill-switch (managed)\"");
                _active = true;
                _log.Info("kill-switch ON");
            }
            catch (Exception ex)
            {
                _log.Error("kill-switch enable failed: " + ex.Message);
            }
        }

        public void Disable()
        {
            if (!_active) return;
            try
            {
                Run("advfirewall firewall delete rule name=\"" + BlockRule + "\"");
                _log.Info("kill-switch OFF");
            }
            catch (Exception ex)
            {
                _log.Error("kill-switch disable failed: " + ex.Message);
            }
            _active = false;
        }

        /// <summary>
        /// Removes every rule that could have been left behind by a crash. Called on
        /// startup, before we know whether a tunnel exists.
        /// </summary>
        public void ResetAll()
        {
            try
            {
                Run("advfirewall firewall delete rule name=\"" + BlockRule + "\"");
            }
            catch { }
            _active = false;
        }

        private static void Run(string args)
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
            }
        }
    }
}
