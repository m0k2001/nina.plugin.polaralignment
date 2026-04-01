using NINA.Core.Utility;
using System;
using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {

    public enum ConnectionMode {
        Serial,
        Tcp
    }

    public abstract partial class UniversalPolarAlignmentBase : IPolarAlignmentSystem {
        private readonly ISerialLike port;

        protected abstract string SystemName { get; }
        protected virtual string NewLineSequence => "\r\n";
        protected virtual int ScanReadTimeout => 1000;
        protected virtual int ScanWriteTimeout => 1000;
        protected virtual bool ClearBufferOnConnect => false;
        protected virtual int PostOpenDelayMs => 0;

        protected abstract Regex GetStatusRegex();

        protected ISerialLike Port => port;

        /// <summary>
        /// Serial constructor: scans all COM ports and auto-detects the device by its status response.
        /// </summary>
        protected UniversalPolarAlignmentBase() {
            var comPorts = SerialPort.GetPortNames();
            foreach (var comPort in comPorts) {
                var serialPort = new SerialPort() {
                    PortName = comPort,
                    BaudRate = 115200,
                    Parity = Parity.None,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    NewLine = NewLineSequence,
                    ReadTimeout = ScanReadTimeout,
                    WriteTimeout = ScanWriteTimeout
                };

                try {
                    serialPort.Open();
                    if (serialPort.IsOpen) {
                        if (PostOpenDelayMs > 0) Thread.Sleep(PostOpenDelayMs);
                        if (ClearBufferOnConnect) serialPort.DiscardInBuffer();

                        serialPort.WriteLine("?");
                        var status = serialPort.ReadLine();
                        _ = serialPort.ReadLine();
                        var match = GetStatusRegex().Match(status);
                        if (match.Success) {
                            port = new SerialPortAdapter(serialPort);
                            Logger.Info($"Found {SystemName} on {comPort}");
                            break;
                        } else {
                            serialPort.Close();
                            serialPort.Dispose();
                        }
                    }
                } catch {
                    serialPort?.Close();
                    serialPort?.Dispose();
                }
            }

            if (port == null) {
                throw new Exception($"Unable to find {SystemName} on any COM port");
            }
            UpdateStatus();
        }

        /// <summary>
        /// TCP constructor: connects directly to a remote host:port exposing the same serial protocol over TCP.
        /// </summary>
        protected UniversalPolarAlignmentBase(string tcpHost, int tcpPort) {
            Logger.Info($"Connecting to {SystemName} via TCP at {tcpHost}:{tcpPort}");
            var adapter = new TcpPortAdapter(tcpHost, tcpPort, ScanReadTimeout, ScanWriteTimeout, NewLineSequence);

            if (PostOpenDelayMs > 0) Thread.Sleep(PostOpenDelayMs);
            if (ClearBufferOnConnect) adapter.DiscardInBuffer();

            adapter.WriteLine("?");
            var status = adapter.ReadLine();
            _ = adapter.ReadLine();
            var match = GetStatusRegex().Match(status);
            if (!match.Success) {
                adapter.Dispose();
                throw new Exception($"Unable to identify {SystemName} at {tcpHost}:{tcpPort} — unexpected response: {status}");
            }

            port = adapter;
            Logger.Info($"Found {SystemName} via TCP at {tcpHost}:{tcpPort}");
            UpdateStatus();
        }

        public bool Connected => port?.IsOpen == true;
        public string Status { get; private set; }

        private float XPosition { get; set; }
        private float YPosition { get; set; }
        private float ZPosition { get; set; }

        public LastDirection XLastDirection { get; private set; } = LastDirection.Positive;
        public LastDirection YLastDirection { get; private set; } = LastDirection.Positive;
        public LastDirection ZLastDirection { get; private set; } = LastDirection.Positive;

        public float XPosition1 { get => XPosition / XGearRatio; }
        public float YPosition1 { get => YPosition / YGearRatio; }
        public float ZPosition1 { get => ZPosition / ZGearRatio; }

        public abstract float XGearRatio { get; set; }
        public abstract float YGearRatio { get; set; }
        public float ZGearRatio { get; set; } = 1;

        private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        public async Task MoveRelative(Axis axis, int speed, float position, CancellationToken token) {
            await semaphore.WaitAsync(token);
            try {
                UpdateStatus();
                var axisCommand = axis switch {
                    Axis.XAxis => "X",
                    Axis.YAxis => "Y",
                    Axis.ZAxis => "Z",
                    _ => throw new ArgumentException("Invalid Axis"),
                };
                var gearRatio = axis switch {
                    Axis.XAxis => XGearRatio,
                    Axis.YAxis => YGearRatio,
                    Axis.ZAxis => ZGearRatio,
                    _ => throw new ArgumentException("Invalid Axis"),
                };

                Func<float> checkProperty = axis switch {
                    Axis.XAxis => () => XPosition,
                    Axis.YAxis => () => YPosition,
                    Axis.ZAxis => () => ZPosition,
                    _ => throw new ArgumentException("Invalid Axis"),
                };

                var target = checkProperty() + position * gearRatio;

                switch (axis) {
                    case Axis.XAxis: XLastDirection = position >= 0 ? LastDirection.Positive : LastDirection.Negative; break;
                    case Axis.YAxis: YLastDirection = position >= 0 ? LastDirection.Positive : LastDirection.Negative; break;
                    case Axis.ZAxis: ZLastDirection = position >= 0 ? LastDirection.Positive : LastDirection.Negative; break;
                }

                var command = $"$J=G91G21{axisCommand}{(position * gearRatio).ToString(CultureInfo.InvariantCulture)}F{speed.ToString(CultureInfo.InvariantCulture)}";
                Logger.Info($"Sending command: {command}");
                port.WriteLine(command);
                var ok = port.ReadLine();
                Logger.Info($"Response: {ok}");

                var startPos = checkProperty();
                var timeout = TimeSpan.FromSeconds(30);
                var startTime = DateTime.Now;
                var lastPos = startPos;
                var stuckCount = 0;

                while (Math.Abs(checkProperty() - target) > 0.01f) {
                    UpdateStatus();
                    var currentPos = checkProperty();
                    if (Math.Abs(currentPos - lastPos) < 0.01f) {
                        stuckCount++;
                        if (stuckCount > 5)
                            throw new TimeoutException($"Motor appears stuck at position {currentPos}. Target was {target}.");
                    } else {
                        stuckCount = 0;
                    }
                    lastPos = currentPos;
                    if (DateTime.Now - startTime > timeout)
                        throw new TimeoutException($"Movement timeout after {timeout.TotalSeconds}s. Current: {currentPos}, Target: {target}");
                    await Task.Delay(300, token);
                }
            } finally {
                semaphore.Release();
            }
        }

        public async Task MoveAbsolute(Axis axis, int speed, float position, CancellationToken token) {
            await semaphore.WaitAsync(token);
            try {
                UpdateStatus();
                var axisCommand = axis switch {
                    Axis.XAxis => "X",
                    Axis.YAxis => "Y",
                    Axis.ZAxis => "Z",
                    _ => throw new ArgumentException("Invalid Axis"),
                };
                var gearRatio = axis switch {
                    Axis.XAxis => XGearRatio,
                    Axis.YAxis => YGearRatio,
                    Axis.ZAxis => ZGearRatio,
                    _ => throw new ArgumentException("Invalid Axis"),
                };

                var target = position * gearRatio;

                switch (axis) {
                    case Axis.XAxis: XLastDirection = position - XPosition1 >= 0 ? LastDirection.Positive : LastDirection.Negative; break;
                    case Axis.YAxis: YLastDirection = position - YPosition1 >= 0 ? LastDirection.Positive : LastDirection.Negative; break;
                    case Axis.ZAxis: ZLastDirection = position - ZPosition1 >= 0 ? LastDirection.Positive : LastDirection.Negative; break;
                }

                var command = $"$J=G53{axisCommand}{target.ToString(CultureInfo.InvariantCulture)}F{speed.ToString(CultureInfo.InvariantCulture)}";
                Logger.Info($"Sending command: {command}");
                port.WriteLine(command);
                var ok = port.ReadLine();
                Logger.Info($"Response: {ok}");

                Func<float> checkProperty = axis switch {
                    Axis.XAxis => () => XPosition,
                    Axis.YAxis => () => YPosition,
                    Axis.ZAxis => () => ZPosition,
                    _ => throw new ArgumentException("Invalid Axis"),
                };

                var timeout = TimeSpan.FromSeconds(30);
                var startTime = DateTime.Now;
                var lastPos = checkProperty();
                var stuckCount = 0;

                while (Math.Abs(checkProperty() - target) > 0.01f) {
                    UpdateStatus();
                    var currentPos = checkProperty();
                    if (Math.Abs(currentPos - lastPos) < 0.01f) {
                        stuckCount++;
                        if (stuckCount > 5)
                            throw new TimeoutException($"Motor appears stuck at position {currentPos}. Target was {target}.");
                    } else {
                        stuckCount = 0;
                    }
                    lastPos = currentPos;
                    if (DateTime.Now - startTime > timeout)
                        throw new TimeoutException($"Movement timeout after {timeout.TotalSeconds}s. Current: {currentPos}, Target: {target}");
                    await Task.Delay(300, token);
                }
            } finally {
                semaphore.Release();
            }
        }

        private void UpdateStatus() {
            port.WriteLine("?");
            var status = port.ReadLine();
            port.ReadLine();

            var match = GetStatusRegex().Match(status);
            if (match.Success) {
                Status = match.Groups["status"].Value;
                XPosition = float.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture);
                YPosition = float.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
                ZPosition = float.Parse(match.Groups["z"].Value, CultureInfo.InvariantCulture);
            } else {
                Logger.Error($"Failed to parse {SystemName} status: {status}");
            }
        }

        public async Task RefreshStatus(CancellationToken token) {
            await semaphore.WaitAsync(token);
            try {
                UpdateStatus();
            } finally {
                semaphore.Release();
            }
        }

        public void Dispose() => port?.Dispose();
    }
}
