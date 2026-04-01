using System.IO;
using System.Net.Sockets;
using System.Text;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Wraps a TcpClient as ISerialLike, compatible with the serial line protocol (WriteLine/ReadLine).
    /// </summary>
    public class TcpPortAdapter : ISerialLike {
        private readonly TcpClient _client;
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private readonly string _newLine;

        public TcpPortAdapter(string host, int port, int readTimeoutMs, int writeTimeoutMs, string newLine = "\n") {
            _newLine = newLine;
            _client = new TcpClient();
            _client.Connect(host, port);
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
