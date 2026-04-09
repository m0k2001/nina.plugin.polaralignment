using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Wraps a TcpClient as ISerialLike, compatible with the serial line protocol (WriteLine/ReadLine).
    /// </summary>
    public class TcpPortAdapter : ISerialLike {
        private readonly TcpClient _client;
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private readonly string _newLine;

        /// <summary>Adresse IP de l'hôte distant.</summary>
        public string RemoteHost { get; }

        /// <summary>Timeout pour l'établissement de la connexion TCP (ms).</summary>
        private const int ConnectTimeoutMs = 5000;

        public TcpPortAdapter(string host, int port, int readTimeoutMs, int writeTimeoutMs, string newLine = "\n") {
            _newLine = newLine;
            RemoteHost = host;
            _client = new TcpClient();
            using var cts = new CancellationTokenSource(ConnectTimeoutMs);
            try {
                _client.ConnectAsync(host, port).Wait(cts.Token);
            } catch (OperationCanceledException) {
                _client.Dispose();
                throw new TimeoutException($"Connection to {host}:{port} timed out after {ConnectTimeoutMs / 1000}s");
            }
            var stream = _client.GetStream();
            stream.ReadTimeout = readTimeoutMs;
            stream.WriteTimeout = writeTimeoutMs;
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = newLine };
            _reader = new StreamReader(stream, Encoding.UTF8);
        }

        /// <summary>Wraps an already-connected TcpClient (used by auto-scan to reuse the probe connection).</summary>
        public TcpPortAdapter(TcpClient existingClient, int readTimeoutMs, int writeTimeoutMs, string newLine = "\n") {
            _newLine = newLine;
            _client = existingClient;
            RemoteHost = (_client.Client.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString();
            var stream = _client.GetStream();
            stream.ReadTimeout = readTimeoutMs;
            stream.WriteTimeout = writeTimeoutMs;
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = newLine };
            _reader = new StreamReader(stream, Encoding.UTF8);
        }

        public bool IsOpen => _client?.Connected == true;

        public void WriteLine(string value) => _writer.WriteLine(value);

        public string ReadLine() {
            // ReadLine strips the newline; mirrors SerialPort.ReadLine behaviour
            return _reader.ReadLine();
        }

        public void DiscardInBuffer() {
            // Drain available bytes without blocking
            var stream = _client.GetStream();
            while (stream.DataAvailable) {
                stream.ReadByte();
            }
        }

        public void Dispose() {
            _writer?.Dispose();
            _reader?.Dispose();
            _client?.Dispose();
        }
    }
}
