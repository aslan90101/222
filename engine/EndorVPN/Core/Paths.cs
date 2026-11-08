using System;
using System.IO;
using System.Reflection;

namespace EndorVPN.Core
{
    /// <summary>
    /// Portable layout:
    ///   EndorVPN.exe
    ///   webview2/            (optional, if the user wants a bundled runtime)
    ///   engine/              openvpn.exe + taps + driver helpers
    ///   ui/                  html/css/js bundle (inside exe when packed)
    ///   data/                settings, logs, server cache (created at runtime)
    /// </summary>
    public sealed class Paths
    {
        public string BaseDir { get; private set; }
        public string EngineDir { get; private set; }
        public string UiDir { get; private set; }
        public string DataDir { get; private set; }
        public string WorkDir { get; private set; }
        public string LogFile { get; private set; }
        public string SettingsFile { get; private set; }
        public string ServerCacheFile { get; private set; }
        public string DriverDir { get; private set; }

        public static Paths Resolve()
        {
            string exe = Assembly.GetEntryAssembly()?.Location ?? "";
            string baseDir;
            if (string.IsNullOrEmpty(exe) || exe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                baseDir = AppDomain.CurrentDomain.BaseDirectory;
            }
            else
            {
                baseDir = Path.GetDirectoryName(exe);
            }
            return new Paths { BaseDir = baseDir };
        }

        public void EnsureLayout()
        {
            EngineDir = Path.Combine(BaseDir, "engine");
            UiDir = Path.Combine(BaseDir, "ui");
            DataDir = Path.Combine(BaseDir, "data");
            WorkDir = Path.Combine(DataDir, "work");
            DriverDir = Path.Combine(EngineDir, "drivers");
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(WorkDir);
            LogFile = Path.Combine(DataDir, "endorvpn.log");
            SettingsFile = Path.Combine(DataDir, "settings.json");
            ServerCacheFile = Path.Combine(DataDir, "servers.cache");
        }

        public void CleanWorkDir()
        {
            try
            {
                if (!Directory.Exists(WorkDir)) return;
                foreach (var f in Directory.GetFiles(WorkDir))
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        public string EngineFile(string name) => Path.Combine(EngineDir, name);
    }
}
