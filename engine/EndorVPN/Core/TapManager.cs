using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EndorVPN.Core
{
    /// <summary>
    /// Everything related to the TAP-Windows6 adapter.
    ///
    /// We install the driver from the copies shipped inside engine/drivers:
    ///   * tap-windows-9.24.7-I601-Win10.exe  (self-extracting installer, /S)
    ///   * tapctl.exe                          (shipped with OpenVPN, used to
    ///                                          create an adapter with our name)
    /// Creating our own adapter means we never touch adapters the user already
    /// has, and uninstalling is a single tapctl command.
    /// </summary>
    public sealed class TapManager
    {
        public const string AdapterName = "EndorVPN";

        private readonly Paths _paths;
        private readonly Log _log;

        public TapManager(Paths paths, Log log)
        {
            _paths = paths;
            _log = log;
        }

        private string TapCtl => Path.Combine(_paths.EngineDir, "tapctl.exe");
        private string Installer => Path.Combine(_paths.EngineDir, "drivers", "tap-windows-installer.exe");

        public bool AdapterExists()
        {
            try
            {
                var psi = new ProcessStartInfo(TapCtl, "list")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    foreach (var line in output.Split('\n'))
                    {
                        if (line.IndexOf(AdapterName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.Warn("tapctl list failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Installs the TAP driver (if needed) and creates/opens our adapter.</summary>
        public bool EnsureAdapter(Action<string> progress)
        {
            if (!File.Exists(TapCtl))
            {
                _log.Error("tapctl missing at " + TapCtl);
                return false;
            }

            if (AdapterExists())
            {
                _log.Info("TAP adapter already present");
                return true;
            }

            // 1st run on this machine: install the signed driver package.
            if (File.Exists(Installer))
            {
                progress?.Invoke("Устанавливаем сетевой драйвер…");
                _log.Info("running TAP installer");
                try
                {
                    var psi = new ProcessStartInfo(Installer, "/S")
                    {
                        UseShellExecute = true,
                        CreateNoWindow = true,
                    };
                    using (var p = Process.Start(psi))
                    {
                        p.WaitForExit(180000);
                    }
                    Thread.Sleep(1500);
                }
                catch (Exception ex)
                {
                    _log.Error("TAP installer failed: " + ex.Message);
                }
            }
            else
            {
                _log.Warn("TAP installer not shipped; relying on existing driver");
            }

            // Create the adapter explicitly with our name so OpenVPN's dev-node is stable.
            try
            {
                progress?.Invoke("Создаём адаптер EndorVPN…");
                var psi = new ProcessStartInfo(TapCtl, "create --name \"" + AdapterName + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.WaitForExit(30000);
                    if (p.ExitCode != 0) _log.Warn("tapctl create exit " + p.ExitCode + ": " + err);
                }
                Thread.Sleep(1200);
            }
            catch (Exception ex)
            {
                _log.Error("tapctl create failed: " + ex.Message);
            }

            return AdapterExists();
        }

        public bool RemoveAdapter()
        {
            try
            {
                var psi = new ProcessStartInfo(TapCtl, "delete \"" + AdapterName + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(20000);
                    _log.Info("tapctl delete exit " + p.ExitCode);
                }
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("tapctl delete failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Best-effort read of the IPv4 address assigned to our adapter.</summary>
        public string GetAdapterIp()
        {
            try
            {
                var log = new StringBuilder();
                var psi = new ProcessStartInfo("ipconfig")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(866),
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);
                    return ExtractAdapterSection(output);
                }
            }
            catch { return null; }
        }

        private string ExtractAdapterSection(string ipconfig)
        {
            bool inSection = false;
            foreach (var raw in ipconfig.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) { inSection = false; continue; }
                if (!char.IsWhiteSpace(raw.Length > 0 ? raw[0] : ' '))
                {
                    inSection = line.IndexOf(AdapterName, StringComparison.OrdinalIgnoreCase) >= 0;
                    continue;
                }
                if (inSection)
                {
                    var i = line.IndexOf("IPv4", StringComparison.OrdinalIgnoreCase);
                    if (i >= 0)
                    {
                        int c = line.LastIndexOf(':');
                        if (c > 0) return line.Substring(c + 1).Trim();
                    }
                }
            }
            return null;
        }
    }
}
