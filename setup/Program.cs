using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace EndorVpnSetup
{
    /// <summary>
    /// Self-extracting launcher for EndorVPN.
    ///
    /// The app needs a native OpenVPN engine (openvpn.exe + TAP driver, ~12 MB)
    /// next to it, so an ILMerged single assembly is impossible. Instead the
    /// portable folder is appended to this stub as a zip payload; on first launch
    /// it unpacks into %LOCALAPPDATA%\EndorVPN and starts the real UI. The file
    /// the user downloads really is one .exe, and later launches skip unpacking.
    ///
    /// Layout: [stub][zip][ASCII marker][int64 payload length]
    /// </summary>
    internal static class Program
    {
        private const string PayloadMarker = "ENDORVPN_PAYLOAD_V1";
        private const string AppExeName = "EndorVPN.exe";

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                string target = InstallDir();
                string markerFile = Path.Combine(target, "payload.version");

                if (!IsInstalled(target, markerFile) && !Extract(target))
                    return 1;

                string exe = Path.Combine(target, AppExeName);
                if (!File.Exists(exe))
                {
                    Error("EndorVPN files are missing:\n" + exe);
                    return 2;
                }

                var psi = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = target };
                foreach (var a in args) psi.Arguments = (psi.Arguments + " " + Quote(a)).Trim();
                Process.Start(psi);
                return 0;
            }
            catch (Exception ex)
            {
                Error("EndorVPN could not start:\n\n" + ex.Message);
                return 3;
            }
        }

        private static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            return s.IndexOf(' ') >= 0 ? "\"" + s + "\"" : s;
        }

        private static string InstallDir()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "EndorVPN");
        }

        private static string Version()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "1.1.1" : v.ToString();
            }
            catch { return "1.1.1"; }
        }

        private static bool IsInstalled(string dir, string markerFile)
        {
            try
            {
                if (!File.Exists(Path.Combine(dir, AppExeName))) return false;
                if (!File.Exists(markerFile)) return false;
                return File.ReadAllText(markerFile).Trim() == Version();
            }
            catch { return false; }
        }

        private static byte[] ReadPayload()
        {
            string self = Assembly.GetExecutingAssembly().Location;
            byte[] all = File.ReadAllBytes(self);
            byte[] marker = Encoding.ASCII.GetBytes(PayloadMarker);

            long length = BitConverter.ToInt64(all, all.Length - 8);
            // Layout at the tail: [zip][marker][int64 length][EOF]
            long markerStart = all.Length - 8 - marker.Length;
            long start = markerStart - length;
            if (start < 0 || length <= 0) throw new InvalidOperationException("installer payload is corrupted");

            for (int j = 0; j < marker.Length; j++)
            {
                if (all[markerStart + j] != marker[j])
                    throw new InvalidOperationException("installer payload offset mismatch");
            }
            // a zip payload always starts with a local file header
            if (length < 4 || all[start] != 0x50 || all[start + 1] != 0x4B)
                throw new InvalidOperationException("installer payload is not a zip archive");

            var payload = new byte[length];
            Buffer.BlockCopy(all, (int)start, payload, 0, (int)length);
            return payload;
        }

        private static bool Extract(string target)
        {
            byte[] payload;
            try { payload = ReadPayload(); }
            catch (Exception ex)
            {
                Error("EndorVPN installer is damaged:\n\n" + ex.Message);
                return false;
            }

            Directory.CreateDirectory(target);
            using (var ms = new MemoryStream(payload))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    string dest = Path.Combine(target, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(dest);
                        continue;
                    }
                    string dir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    entry.ExtractToFile(dest, true);
                }
            }
            File.WriteAllText(Path.Combine(target, "payload.version"), Version());
            return true;
        }

        private static void Error(string message)
        {
            try
            {
                // no UI framework in this stub: use the native message box
                MessageBoxW(IntPtr.Zero, message, "EndorVPN", 0x00000010);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    }
}
