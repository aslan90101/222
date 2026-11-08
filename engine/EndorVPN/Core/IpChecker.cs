using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EndorVPN.Core
{
    public sealed class IpInfo
    {
        public string Ip { get; set; }
        public string CountryCode { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public string Isp { get; set; }
        public bool Ok { get { return !string.IsNullOrEmpty(Ip); } }
    }

    /// <summary>
    /// Asks several public "what is my IP" endpoints and returns the first answer.
    /// Endpoints are tried in parallel-ish order; we never trust a single provider.
    /// </summary>
    public sealed class IpChecker
    {
        private static readonly string[] JsonUrls =
        {
            "https://api.ipify.org?format=json",
            "https://ipinfo.io/json",
            "https://api.myip.com",
            "https://ifconfig.me/all.json",
        };

        private readonly Log _log;
        private readonly HttpClient _client;

        public IpChecker(Log log)
        {
            _log = log;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(9) };
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "EndorVPN/1.0");
        }

        public async Task<IpInfo> GetAsync(CancellationToken ct)
        {
            foreach (var url in JsonUrls)
            {
                if (ct.IsCancellationRequested) return null;
                try
                {
                    string body = await _client.GetStringAsync(url).ConfigureAwait(false);
                    var info = ParseJson(body);
                    if (info.Ok)
                    {
                        _log.Info("ip via " + url + " => " + info.Ip + " (" + info.CountryCode + ")");
                        return info;
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("ip url failed " + url + ": " + ex.Message);
                }
            }
            return new IpInfo();
        }

        private static IpInfo ParseJson(string body)
        {
            var info = new IpInfo();
            string ip = JStr(body, "ip");
            if (string.IsNullOrEmpty(ip)) ip = JStr(body, "query");
            if (string.IsNullOrEmpty(ip)) return info;
            IPAddress parsed;
            if (!IPAddress.TryParse(ip, out parsed)) return info;
            info.Ip = ip;
            info.CountryCode = (JStr(body, "country_code") ?? JStr(body, "country") ?? "").ToUpperInvariant();
            if (info.CountryCode.Length > 2) info.CountryCode = info.CountryCode.Substring(0, 2);
            info.Country = JStr(body, "country_name") ?? JStr(body, "country");
            info.City = JStr(body, "city");
            info.Isp = JStr(body, "isp") ?? JStr(body, "org");
            return info;
        }

        private static readonly Regex JsonString = new Regex("\"([A-Za-z_]+)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
        private static readonly Regex JsonNumberKey = new Regex("\"([A-Za-z_]+)\"\\s*:\\s*(-?\\d+)", RegexOptions.Compiled);

        private static string JStr(string json, string key)
        {
            foreach (Match m in JsonString.Matches(json))
            {
                if (m.Groups[1].Value == key) return Regex.Unescape(m.Groups[2].Value);
            }
            return null;
        }

        /// <summary>Quick TCP reachability probe used to rank servers without full connects.</summary>
        public static int TcpPing(string host, int port, int timeoutMs)
        {
            try
            {
                var client = new System.Net.Sockets.TcpClient();
                var sw = Stopwatch.StartNew();
                var task = client.ConnectAsync(host, port);
                if (!task.Wait(timeoutMs)) { client.Close(); return -1; }
                sw.Stop();
                client.Close();
                return (int)sw.ElapsedMilliseconds;
            }
            catch
            {
                return -1;
            }
        }
    }
}
