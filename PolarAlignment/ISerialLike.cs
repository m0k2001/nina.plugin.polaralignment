using System;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Abstraction over SerialPort and TcpClient to allow both COM and TCP connections.
    /// </summary>
    public interface ISerialLike : IDisposable {
        bool IsOpen { get; }
        void WriteLine(string value);
        string ReadLine();
        void DiscardInBuffer();
    }
}
