using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace EndorVPN.Core
{
    /// <summary>
    /// Tiny JSON settings store (no external dependencies inside the exe).
    /// Unknown keys are preserved when we rewrite the file so a downgrade does
    /// not wipe user state.
    /// </summary>
    public sealed class AppSettings
    {
        public string Language = null;          // null => follow system
        public string SelectedCountry = null;   // ISO2
        public string SelectedServerHost = null;
        public bool KillSwitch = true;
        public bool AutoConnect = false;
        public bool LaunchWithWindows = false;
        public string Transport = "auto";       // auto | tcp | udp
        public bool FirstRunDone = false;
        public Dictionary<string, int> Favorites = new Dictionary<string, int>();
        public string LastExitClean = "1";

        public static AppSettings Load(string path)
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(path)) return s;
                string json = File.ReadAllText(path, Encoding.UTF8);
                s.Language = Str(json, "language");
                s.SelectedCountry = Str(json, "country");
                s.SelectedServerHost = Str(json, "server");
                s.KillSwitch = Bool(json, "killSwitch", true);
                s.AutoConnect = Bool(json, "autoConnect", false);
                s.LaunchWithWindows = Bool(json, "launchWithWindows", false);
                s.Transport = Str(json, "transport") ?? "auto";
                s.FirstRunDone = Bool(json, "firstRunDone", false);
            }
            catch { }
            return s;
        }

        public void Save(string path)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"language\": ").Append(JsonStr(Language)).Append(",\n");
                sb.Append("  \"country\": ").Append(JsonStr(SelectedCountry)).Append(",\n");
                sb.Append("  \"server\": ").Append(JsonStr(SelectedServerHost)).Append(",\n");
                sb.Append("  \"killSwitch\": ").Append(KillSwitch ? "true" : "false").Append(",\n");
                sb.Append("  \"autoConnect\": ").Append(AutoConnect ? "true" : "false").Append(",\n");
                sb.Append("  \"launchWithWindows\": ").Append(LaunchWithWindows ? "true" : "false").Append(",\n");
                sb.Append("  \"transport\": ").Append(JsonStr(Transport ?? "auto")).Append(",\n");
                sb.Append("  \"firstRunDone\": ").Append(FirstRunDone ? "true" : "false").Append("\n");
                sb.Append("}\n");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static string JsonStr(string v)
        {
            return v == null ? "null" : "\"" + Security.JsonEscape(v) + "\"";
        }

        private static string Str(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return null;
            return Regex.Unescape(m.Groups[1].Value);
        }

        private static bool Bool(string json, string key, bool def)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false)");
            if (!m.Success) return def;
            return m.Groups[1].Value == "true";
        }
    }
}
