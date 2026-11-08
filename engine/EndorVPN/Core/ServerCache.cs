using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EndorVPN.Core
{
    /// <summary>
    /// Encrypted store for VPN Gate server configurations.
    ///
    /// Why: VPN Gate configs are public, but a plaintext cache of a few hundred
    /// .ovpn files on disk is noisy and can be tampered with. We keep the whole
    /// server list in a single AES-256-CBC + HMAC-SHA256 blob (encrypt-then-MAC,
    /// random IV per write). The key derives from a per-install value, so a
    /// stolen cache is useless on another machine.
    /// </summary>
    public sealed class ServerCache
    {
        private readonly string _path;
        private readonly Log _log;
        private readonly object _sync = new object();
        private byte[] _key;

        public ServerCache(string path, Log log)
        {
            _path = path;
            _log = log;
        }

        private byte[] Key
        {
            get
            {
                if (_key != null) return _key;
                // Per-install key material lives next to the cache with a hidden
                // name; if it is missing we generate it once.
                string keyFile = _path + ".k";
                try
                {
                    if (File.Exists(keyFile))
                    {
                        _key = File.ReadAllBytes(keyFile);
                        if (_key.Length == 32) return _key;
                    }
                }
                catch { }
                using (var rng = new RNGCryptoServiceProvider())
                {
                    _key = new byte[32];
                    rng.GetBytes(_key);
                }
                try
                {
                    File.WriteAllBytes(keyFile, _key);
                    var attrs = File.GetAttributes(keyFile);
                    File.SetAttributes(keyFile, attrs | FileAttributes.Hidden);
                }
                catch { }
                return _key;
            }
        }

        public void Save(string plainJson)
        {
            try
            {
                lock (_sync)
                {
                    byte[] data = Encoding.UTF8.GetBytes(plainJson);
                    byte[] blob = Encrypt(data);
                    string tmp = _path + ".tmp";
                    File.WriteAllBytes(tmp, blob);
                    if (File.Exists(_path)) File.Delete(_path);
                    File.Move(tmp, _path);
                }
                _log.Info("server cache saved, " + plainJson.Length + " chars");
            }
            catch (Exception ex)
            {
                _log.Warn("cache save failed: " + ex.Message);
            }
        }

        public string Load()
        {
            try
            {
                lock (_sync)
                {
                    if (!File.Exists(_path)) return null;
                    byte[] blob = File.ReadAllBytes(_path);
                    byte[] data = Decrypt(blob);
                    return data == null ? null : Encoding.UTF8.GetString(data);
                }
            }
            catch (Exception ex)
            {
                _log.Warn("cache load failed: " + ex.Message);
                return null;
            }
        }

        private byte[] Encrypt(byte[] data)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = Key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();
                byte[] cipher;
                using (var enc = aes.CreateEncryptor()) cipher = enc.TransformFinalBlock(data, 0, data.Length);
                using (var hmac = new HMACSHA256(Key))
                {
                    byte[] mac = hmac.ComputeHash(Concat(aes.IV, cipher));
                    return Concat(Encoding.ASCII.GetBytes("EVPN1"), aes.IV, cipher, mac);
                }
            }
        }

        private byte[] Decrypt(byte[] blob)
        {
            if (blob.Length < 5 + 16 + 32) return null;
            if (Encoding.ASCII.GetString(blob, 0, 5) != "EVPN1") return null;
            byte[] iv = new byte[16];
            Buffer.BlockCopy(blob, 5, iv, 0, 16);
            int cipherLen = blob.Length - 5 - 16 - 32;
            byte[] cipher = new byte[cipherLen];
            Buffer.BlockCopy(blob, 5 + 16, cipher, 0, cipherLen);
            byte[] mac = new byte[32];
            Buffer.BlockCopy(blob, blob.Length - 32, mac, 0, 32);
            using (var hmac = new HMACSHA256(Key))
            {
                byte[] expect = hmac.ComputeHash(Concat(iv, cipher));
                if (!FixedTimeEquals(mac, expect)) return null;
            }
            using (var aes = Aes.Create())
            {
                aes.Key = Key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.IV = iv;
                using (var dec = aes.CreateDecryptor()) return dec.TransformFinalBlock(cipher, 0, cipher.Length);
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int len = 0;
            foreach (var p in parts) len += p.Length;
            var outb = new byte[len];
            int off = 0;
            foreach (var p in parts)
            {
                Buffer.BlockCopy(p, 0, outb, off, p.Length);
                off += p.Length;
            }
            return outb;
        }
    }
}
