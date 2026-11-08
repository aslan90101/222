using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace EndorVPN
{
    /// <summary>
    /// Named mutex + a tiny local IPC pipe so a second launch just brings the
    /// existing window to front instead of starting a competing tunnel.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private readonly string _name;
        private EventWaitHandle _event;
        private Thread _listener;
        private volatile bool _stop;
        private Mutex _mutex;

        public event Action ActivateRequested;

        public SingleInstance(string name)
        {
            _name = "Local\\" + name;
        }

        public bool TryAcquire()
        {
            try
            {
                _mutex = new Mutex(true, _name + ".mutex", out bool created);
                if (!created) return false;
                _event = new EventWaitHandle(false, EventResetMode.AutoReset, _name + ".activate");
                _listener = new Thread(ListenLoop) { IsBackground = true, Name = "single-instance" };
                _listener.Start();
                return true;
            }
            catch
            {
                return true;
            }
        }

        private void ListenLoop()
        {
            while (!_stop)
            {
                try
                {
                    if (_event.WaitOne(500))
                    {
                        var h = ActivateRequested;
                        if (h != null) h();
                    }
                }
                catch { break; }
            }
        }

        public void SignalRunningInstance()
        {
            try
            {
                var ev = EventWaitHandle.OpenExisting("Local\\" + _name.Replace("Local\\\\", "") + ".activate");
                ev.Set();
            }
            catch { }
        }

        public void Dispose()
        {
            _stop = true;
            try { _event?.Set(); } catch { }
            try { _event?.Dispose(); } catch { }
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
        }
    }
}
