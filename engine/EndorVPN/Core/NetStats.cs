using System;
using System.Net.NetworkInformation;

namespace EndorVPN.Core
{
    /// <summary>
    /// Samples the TAP adapter's byte counters to show live up/down speed.
    /// Uses the IPv4 statistics of the adapter with our exact name, so figures
    /// are real (tunnel traffic only), not guesswork.
    /// </summary>
    public sealed class NetStats
    {
        private readonly Log _log;
        private string _adapter;
        private long _lastIn, _lastOut;
        private DateTime _lastSample = DateTime.MinValue;
        private long _totalIn, _totalOut;

        public double DownMbps { get; private set; }
        public double UpMbps { get; private set; }
        public long TotalBytes { get { return _totalIn + _totalOut; } }
        public bool Available { get; private set; }

        public NetStats(Log log)
        {
            _log = log;
        }

        public void Reset()
        {
            _lastIn = _lastOut = 0;
            _lastSample = DateTime.MinValue;
            _totalIn = _totalOut = 0;
            DownMbps = UpMbps = 0;
        }

        public void Sample()
        {
            try
            {
                if (_adapter == null) _adapter = FindAdapter();
                if (_adapter == null) { Available = false; return; }

                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!string.Equals(ni.Name, _adapter, StringComparison.OrdinalIgnoreCase)) continue;
                    var s = ni.GetIPv4Statistics();
                    long inB = s.BytesReceived;
                    long outB = s.BytesSent;
                    var now = DateTime.UtcNow;
                    if (_lastSample != DateTime.MinValue && (now - _lastSample).TotalMilliseconds > 50)
                    {
                        double dt = (now - _lastSample).TotalSeconds;
                        DownMbps = Math.Max(0, ((inB - _lastIn) * 8.0 / 1e6) / dt);
                        UpMbps = Math.Max(0, ((outB - _lastOut) * 8.0 / 1e6) / dt);
                    }
                    _lastIn = inB;
                    _lastOut = outB;
                    _totalIn = inB;
                    _totalOut = outB;
                    _lastSample = now;
                    Available = true;
                    return;
                }
                Available = false;
            }
            catch (Exception ex)
            {
                _log.Warn("netstat sample failed: " + ex.Message);
            }
        }

        private string FindAdapter()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.Name.IndexOf(TapManager.AdapterName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ni.Description.IndexOf("TAP-Windows", StringComparison.OrdinalIgnoreCase) >= 0)
                        return ni.Name;
                }
            }
            catch { }
            return null;
        }
    }
}
