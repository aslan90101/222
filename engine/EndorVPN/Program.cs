using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using EndorVPN.Core;

namespace EndorVPN
{
    /// <summary>
    /// Process entry point: portable layout, single instance, settings, then the
    /// WPF shell. Elevated start is requested by the UI when it needs the driver.
    /// </summary>
    public static class Program
    {
        public const string AppName = "EndorVPN";
        public const string Version = "1.2.0";

        public static Paths Paths { get; private set; }
        public static AppSettings Settings { get; private set; }
        public static Log Log { get; private set; }
        public static SingleInstance Instance { get; private set; }
        public static TapManager Tap { get; private set; }

        [STAThread]
        public static void Main(string[] args)
        {
            bool smokeTest = args.Any(a => a.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase));
            bool e2e = args.Any(a => a.Equals("--e2e-connect", StringComparison.OrdinalIgnoreCase));
            bool minimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            Paths = Paths.Resolve();
            Paths.EnsureLayout();
            Log = new Log(Paths.LogFile);
            Log.Info("=== " + AppName + " " + Version + " starting (admin=" + Security.IsAdministrator() + ") ===");

            if (smokeTest)
            {
                int code = SmokeTest.Run(Paths, Log);
                Log.Info("smoke exit " + code);
                Environment.Exit(code);
                return;
            }

            if (e2e)
            {
                int code = E2eTest.Run(Paths, Log).GetAwaiter().GetResult();
                Log.Info("e2e exit " + code);
                Environment.Exit(code);
                return;
            }

            Instance = new SingleInstance("EndorVPN_" + Math.Abs(Paths.BaseDir.ToLowerInvariant().GetHashCode()));
            if (!Instance.TryAcquire())
            {
                Log.Info("already running, signalling the existing instance");
                Instance.SignalRunningInstance();
                return;
            }

            Settings = AppSettings.Load(Paths.SettingsFile);
            Tap = new TapManager(Paths, Log);

            var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log.Error("dispatcher: " + e.Exception);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("unhandled: " + e.ExceptionObject);

            var window = new MainWindow();
            if (minimized) window.WindowState = WindowState.Minimized;
            app.MainWindow = window;
            Instance.ActivateRequested += () => window.Dispatcher.BeginInvoke((Action)(() =>
            {
                window.Show();
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
            }));
            app.Run(window);
        }

        public static void ProcessTapRemove()
        {
            try
            {
                bool ok = Tap != null && Tap.RemoveAdapter();
                Log.Info("tap remove: " + ok);
            }
            catch (Exception ex) { Log.Error("tap remove failed: " + ex.Message); }
        }
    }
}
