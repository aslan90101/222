using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EndorVpnSetup
{
    /// <summary>
    /// Self-extracting launcher for EndorVPN.
    ///
    /// Shipping constraints: the app needs a real OpenVPN engine (openvpn.exe +
    /// TAP driver, ~12 MB of native binaries) next to it, so an ILMerged single
    /// assembly is impossible. Instead the portable folder is appended to this
    /// stub as a zip payload; on launch it unpacks once into
    /// %LOCALAPPDATA%\EndorVPN and starts the real UI, so the thing the user
    /// downloads really is a single .exe.
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

                if (!IsInstalled(target, markerFile))
                {
                    if (!ExtractWithProgress(target))
                        return 1;
                }

                string exe = Path.Combine(target, AppExeName);
                if (!File.Exists(exe))
                {
                    MessageBox.Show("EndorVPN files are missing: " + exe, "EndorVPN", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 2;
                }

                var psi = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = target };
                foreach (var a in args) psi.Arguments = (psi.Arguments + " " + Quote(a)).Trim();
                Process.Start(psi);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("EndorVPN could not start:\n\n" + ex.Message, "EndorVPN", MessageBoxButton.OK, MessageBoxImage.Error);
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
                return v == null ? "1.0.0" : v.ToString();
            }
            catch { return "1.0.0"; }
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
            // The marker is written immediately after the compressed stream;
            // search from the end so a stray match inside the zip is ignored.
            int idx = -1;
            for (int i = all.Length - marker.Length - 8; i >= 0; i--)
            {
                bool hit = true;
                for (int j = 0; j < marker.Length; j++)
                {
                    if (all[i + j] != marker[j]) { hit = false; break; }
                }
                if (hit) { idx = i; break; }
            }
            if (idx < 0) throw new InvalidOperationException("installer payload not found");

            // last 8 bytes hold the payload length as little-endian int64
            long length = BitConverter.ToInt64(all, all.Length - 8);
            long start = all.Length - 8 - length;
            if (start < 0 || length <= 0) throw new InvalidOperationException("installer payload is corrupted");
            if (start != idx) throw new InvalidOperationException("installer payload offset mismatch");

            var payload = new byte[length];
            Buffer.BlockCopy(all, (int)start, payload, 0, (int)length);
            return payload;
        }

        private static bool ExtractWithProgress(string target)
        {
            byte[] payload;
            try { payload = ReadPayload(); }
            catch (Exception ex)
            {
                MessageBox.Show("EndorVPN installer is damaged:\n\n" + ex.Message, "EndorVPN", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            var win = new Window
            {
                Title = "EndorVPN",
                Width = 420,
                Height = 210,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
            };

            var card = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x11, 0x20)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x78, 0xAA, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(24, 20, 24, 20),
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "EndorVPN",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF0, 0xFF)),
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Устанавливаю файлы… / Unpacking files…",
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 14),
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x71, 0x8E)),
            });
            var bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 100, Value = 0 };
            stack.Children.Add(bar);
            var fileLabel = new TextBlock
            {
                Text = "",
                FontSize = 10.5,
                Margin = new Thickness(0, 10, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x71, 0x8E)),
            };
            stack.Children.Add(fileLabel);
            card.Child = stack;
            win.Content = card;

            bool done = false;
            win.Loaded += (s, e) =>
            {
                try
                {
                    Directory.CreateDirectory(target);
                    using (var ms = new MemoryStream(payload))
                    using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                    {
                        int total = zip.Entries.Count;
                        int n = 0;
                        foreach (var entry in zip.Entries)
                        {
                            n++;
                            string dest = Path.Combine(target, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                Directory.CreateDirectory(dest);
                            }
                            else
                            {
                                string dir = Path.GetDirectoryName(dest);
                                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                                entry.ExtractToFile(dest, true);
                            }
                            bar.Value = total == 0 ? 100 : (n * 100.0 / total);
                            if (n % 4 == 0 || n == total) fileLabel.Text = entry.FullName;
                        }
                    }
                    File.WriteAllText(Path.Combine(target, "payload.version"), Version());
                    bar.Value = 100;
                    done = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unpacking failed:\n\n" + ex.Message, "EndorVPN", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (s, e) =>
            {
                if (!done) return;
                timer.Stop();
                win.Close();
            };
            timer.Start();
            win.ShowDialog();
            return done;
        }
    }
}
