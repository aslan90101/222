using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace EndorVPN.Core
{
    /// <summary>
    /// Thread-safe file logger with a bounded in-memory ring buffer that the UI
    /// log drawer shows. Never throws: logging must not be able to kill the app.
    /// </summary>
    public sealed class Log
    {
        private readonly object _sync = new object();
        private readonly string _path;
        private readonly Queue<string> _ring = new Queue<string>(512);
        private const int MaxFileBytes = 2 * 1024 * 1024;

        public Log(string path)
        {
            _path = path;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (dir != null) Directory.CreateDirectory(dir);
                if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
                {
                    var backup = path + ".old";
                    try { File.Delete(backup); } catch { }
                    try { File.Move(path, backup); } catch { }
                }
            }
            catch { }
        }

        public event Action<string> Appended;

        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("WARN", message);
        public void Error(string message) => Write("ERROR", message);

        public void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level + "] " + message;
            try
            {
                lock (_sync)
                {
                    File.AppendAllText(_path, DateTime.Now.ToString("yyyy-MM-dd ") + line + Environment.NewLine, Encoding.UTF8);
                    _ring.Enqueue(line);
                    while (_ring.Count > 500) _ring.Dequeue();
                }
            }
            catch { }
            try { Appended?.Invoke(line); } catch { }
        }

        public string Tail()
        {
            lock (_sync) return string.Join(Environment.NewLine, _ring);
        }
    }
}
