using System.IO.Ports;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Wraps a SerialPort as ISerialLike.
    /// </summary>
    public class SerialPortAdapter : ISerialLike {
        private readonly SerialPort _port;

        public SerialPortAdapter(SerialPort port) {
            _port = port;
        }

        public bool IsOpen => _port.IsOpen;
        public void WriteLine(string value) => _port.WriteLine(value);
        public string ReadLine() => _port.ReadLine();
        public void DiscardInBuffer() => _port.DiscardInBuffer();
        public void Dispose() => _port?.Dispose();
    }
}
