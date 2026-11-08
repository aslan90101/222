using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
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
    /// Resolves the public egress IP through several independent providers so one
    /// blocked/unavailable endpoint cannot leave the UI showing "unknown".
    /// Plain-text endpoints are parsed with a regex; JSON ones with a tiny reader.
    /// </summary>
    public sealed class IpChecker
    {
        private sealed class Prov
        {
            public string Url;
            public bool Json;
            public Prov(string u, bool json) { Url = u; Json = json; }
        }

        private static readonly Prov[] Providers =
        {
            new Prov("http://ip-api.com/json/?fields=status,country,countryCode,city,isp,query", true),
            new Prov("https://api.ipify.org?format=json", true),
            new Prov("https://ipinfo.io/json", true),
            new Prov("https://api.myip.com", true),
            new Prov("https://checkip.amazonaws.com", false),
            new Prov("https://icanhazip.com", false),
            new Prov("https://ifconfig.me/ip", false),
        };

        private readonly Log _log;
        private readonly HttpClient _client;

        public IpChecker(Log log)
        {
            _log = log;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "EndorVPN/1.0");
        }

        public async Task<IpInfo> GetAsync(CancellationToken ct)
        {
            foreach (var p in Providers)
            {
                if (ct.IsCancellationRequested) return null;
                try
                {
                    string body = (await _client.GetStringAsync(p.Url).ConfigureAwait(false) ?? "").Trim();
                    var info = p.Json ? ParseJson(body) : ParsePlain(body);
                    if (info.Ok)
                    {
                        _log.Info("ip " + info.Ip + (string.IsNullOrEmpty(info.CountryCode) ? "" : " (" + info.CountryCode + ")") + " via " + p.Url);
                        return info;
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("ip provider failed " + p.Url + ": " + ex.Message);
                }
            }
            _log.Warn("all ip providers failed");
            return new IpInfo();
        }

        private static IpInfo ParsePlain(string body)
        {
            var info = new IpInfo();
            var m = Regex.Match(body ?? "", @"\b(\d{1,3}(?:\.\d{1,3}){3})\b");
            if (m.Success) info.Ip = m.Groups[1].Value;
            return info;
        }

        private static IpInfo ParseJson(string body)
        {
            var info = new IpInfo();
            string ip = JStr(body, "ip") ?? JStr(body, "query");
            if (string.IsNullOrEmpty(ip)) return info;
            IPAddress parsed;
            if (!IPAddress.TryParse(ip, out parsed)) return info;
            info.Ip = ip;
            string cc = JStr(body, "country_code") ?? JStr(body, "countryCode") ?? JStr(body, "country") ?? "";
            cc = cc.ToUpperInvariant();
            if (cc.Length > 2) cc = cc.Substring(0, 2);
            info.CountryCode = cc;
            info.Country = JStr(body, "country_name") ?? JStr(body, "country");
            info.City = JStr(body, "city");
            info.Isp = JStr(body, "isp") ?? JStr(body, "org");
            return info;
        }

        private static readonly Regex JsonString = new Regex("\"([A-Za-z_]+)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        private static string JStr(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            foreach (Match m in JsonString.Matches(json))
            {
                if (m.Groups[1].Value == key) return Regex.Unescape(m.Groups[2].Value);
            }
            return null;
        }

        /// <summary>TCP connect time probe used to rank servers before connecting.</summary>
        public static int TcpPing(string host, int port, int timeoutMs)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var sw = Stopwatch.StartNew();
                    var task = client.ConnectAsync(host, port);
                    if (!task.Wait(timeoutMs)) return -1;
                    sw.Stop();
                    return (int)sw.ElapsedMilliseconds;
                }
            }
            catch { return -1; }
        }
    }
}
