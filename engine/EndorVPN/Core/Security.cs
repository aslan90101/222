using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace EndorVPN.Core
{
    public static class Security
    {
        public static bool IsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        /// <summary>Re-launches this executable elevated through the shell (UAC prompt).</summary>
        public static bool RestartElevated(string args = "")
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = System.Reflection.Assembly.GetEntryAssembly().Location,
                    Arguments = args ?? "",
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Escapes a value for a JSON string literal.</summary>
        public static string JsonEscape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
