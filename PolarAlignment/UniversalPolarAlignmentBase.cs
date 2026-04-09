using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {

    public enum ConnectionMode {
        Serial,
        Tcp,
        AutoTcp
    }

    public abstract partial class UniversalPolarAlignmentBase : IPolarAlignmentSystem {
        private readonly ISerialLike port;

        protected abstract string SystemName { get; }
        protected virtual string NewLineSequence => "\r\n";
        protected virtual int ScanReadTimeout => 1000;
        protected virtual int ScanWriteTimeout => 1000;
        protected virtual bool ClearBufferOnConnect => false;
        protected virtual int PostOpenDelayMs => 0;

        /// <summary>Timeout par hôte lors du scan TCP automatique (ms).</summary>
        protected virtual int AutoScanTimeoutMs => 500;

        /// <summary>Hostname mDNS du device (sans .local). Null pour désactiver la résolution mDNS.</summary>
        protected virtual string MdnsHostname => null;

        /// <summary>Dernière IP connue persistée. Retourne null/vide si aucune.</summary>
        protected virtual string GetLastKnownIp() => null;
        /// <summary>Persiste l'IP après une connexion AutoTcp réussie.</summary>
        protected virtual void SaveLastKnownIp(string ip) { }

        /// <summary>Dernier port COM connu persisté. Retourne null/vide si aucun.</summary>
        protected virtual string GetLastKnownComPort() => null;
        /// <summary>Persiste le port COM après une connexion série réussie.</summary>
        protected virtual void SaveLastKnownComPort(string comPort) { }

        protected abstract Regex GetStatusRegex();
        protected ISerialLike Port => port;

        // -----------------------------------------------------------------------
        // Constructeur Serial : scan auto des ports COM
        // -----------------------------------------------------------------------
        protected UniversalPolarAlignmentBase() {
            var allPorts = SerialPort.GetPortNames();
            string foundOnPort = null;

            // Étape 1 : tester le dernier port COM connu
            var lastKnown = GetLastKnownComPort();
            if (!string.IsNullOrEmpty(lastKnown) && allPorts.Contains(lastKnown)) {
                Logger.Info($"Trying last known COM port: {lastKnown}");
                var result = TryProbeSerialPort(lastKnown);
                if (result != null) {
                    port = result;
                    foundOnPort = lastKnown;
                }
            }

            // Étape 2 : scanner les autres ports en parallèle
            if (port == null) {
                var remainingPorts = allPorts.Where(p => p != lastKnown).ToArray();
                if (remainingPorts.Length > 0) {
                    Logger.Info($"Scanning {remainingPorts.Length} COM ports in parallel...");
                    var results = new ISerialLike[remainingPorts.Length];
                    var tasks = remainingPorts.Select((comPort, index) => Task.Run(() => {
                        results[index] = TryProbeSerialPort(comPort);
                    })).ToArray();
                    Task.WaitAll(tasks);

                    for (int i = 0; i < remainingPorts.Length; i++) {
                        if (results[i] != null) {
                            port = results[i];
                            foundOnPort = remainingPorts[i];
                            // Fermer les autres ports trouvés
                            for (int j = i + 1; j < remainingPorts.Length; j++)
                                results[j]?.Dispose();
                            break;
                        }
                    }
                }
            }

            if (port == null) throw new Exception($"Unable to find {SystemName} on any COM port");

            Logger.Info($"Found {SystemName} on {foundOnPort}");
            SaveLastKnownComPort(foundOnPort);
            UpdateStatus();
        }

        private ISerialLike TryProbeSerialPort(string comPort) {
            var serialPort = new SerialPort() {
                PortName = comPort,
                BaudRate = 115200,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                DtrEnable = false,
                RtsEnable = false,
                NewLine = NewLineSequence,
                ReadTimeout = ScanReadTimeout,
                WriteTimeout = ScanWriteTimeout
            };
            try {
                serialPort.Open();
                if (!serialPort.IsOpen) { serialPort.Dispose(); return null; }
                if (PostOpenDelayMs > 0) Thread.Sleep(PostOpenDelayMs);
                if (ClearBufferOnConnect) serialPort.DiscardInBuffer();
                serialPort.WriteLine("?");
                var status = serialPort.ReadLine();
                _ = serialPort.ReadLine();
                if (GetStatusRegex().Match(status).Success) {
                    return new SerialPortAdapter(serialPort);
                }
                serialPort.Close();
                serialPort.Dispose();
            } catch {
                serialPort?.Close();
                serialPort?.Dispose();
            }
            return null;
        }

        // -----------------------------------------------------------------------
        // Constructeur TCP manuel : connexion directe host:port
        // -----------------------------------------------------------------------
        protected UniversalPolarAlignmentBase(string tcpHost, int tcpPort) {
            Logger.Info($"Connecting to {SystemName} via TCP at {tcpHost}:{tcpPort}");
            var adapter = new TcpPortAdapter(tcpHost, tcpPort, ScanReadTimeout, ScanWriteTimeout, NewLineSequence);
            if (PostOpenDelayMs > 0) Thread.Sleep(PostOpenDelayMs);
            if (ClearBufferOnConnect) adapter.DiscardInBuffer();
            adapter.WriteLine("?");
            var status = adapter.ReadLine();
            _ = adapter.ReadLine();
            if (!GetStatusRegex().Match(status).Success) {
                adapter.Dispose();
                throw new Exception($"Unable to identify {SystemName} at {tcpHost}:{tcpPort} — response: {status}");
            }
            port = adapter;
            RemoteEndpoint = $"{tcpHost}:{tcpPort}";
            Logger.Info($"Found {SystemName} via TCP at {tcpHost}:{tcpPort}");
            UpdateStatus();
        }

        // -----------------------------------------------------------------------
        // Constructeur Auto-TCP : last IP → mDNS → ARP → scan complet
        // -----------------------------------------------------------------------
        protected UniversalPolarAlignmentBase(int tcpPort) {
            Logger.Info($"Auto-discovering {SystemName} on port {tcpPort}...");

            ISerialLike found = null;
            string foundIp = null;

            // Étape 0 : dernière IP connue
            var lastIp = GetLastKnownIp();
            if (!string.IsNullOrEmpty(lastIp)) {
                Logger.Info($"Trying last known IP: {lastIp}:{tcpPort}");
                found = TryDirectConnect(lastIp, tcpPort);
                if (found != null) foundIp = lastIp;
            }

            // Étape 1 : résolution mDNS
            if (found == null && MdnsHostname != null) {
                found = TryMdnsResolve(tcpPort, out foundIp);
            }

            // Étape 2 : ARP table — ne tester que les hôtes déjà connus
            if (found == null) {
                Logger.Info("mDNS failed or disabled, trying ARP table...");
                found = TryArpScanAsync(tcpPort, CancellationToken.None)
                            .GetAwaiter().GetResult();
            }

            // Étape 3 : scan complet du subnet
            if (found == null) {
                Logger.Info("ARP scan found nothing, falling back to full subnet scan...");
                found = ScanAllSubnetsAsync(tcpPort, CancellationToken.None)
                            .GetAwaiter().GetResult();
            }

            if (found == null)
                throw new Exception($"Unable to find {SystemName} on any network interface (port {tcpPort})");

            port = found;

            // Persister l'IP pour le prochain lancement
            if (foundIp == null && port is TcpPortAdapter tcpAdapter) {
                foundIp = tcpAdapter.RemoteHost;
            }
            if (!string.IsNullOrEmpty(foundIp)) {
                RemoteEndpoint = $"{foundIp}:{tcpPort}";
                Logger.Info($"Saving last known IP: {foundIp}");
                SaveLastKnownIp(foundIp);
            }

            Logger.Info($"Auto-discovery found {SystemName}");
            if (ClearBufferOnConnect) port.DiscardInBuffer();
            UpdateStatus();
        }

        // -----------------------------------------------------------------------
        // Étape 0 : connexion directe sur une IP connue
        // -----------------------------------------------------------------------
        private ISerialLike TryDirectConnect(string host, int tcpPort) {
            try {
                var adapter = new TcpPortAdapter(host, tcpPort, ScanReadTimeout, ScanWriteTimeout, NewLineSequence);
                if (PostOpenDelayMs > 0) Thread.Sleep(PostOpenDelayMs);
                if (ClearBufferOnConnect) adapter.DiscardInBuffer();
                adapter.WriteLine("?");
                var status = adapter.ReadLine();
                _ = adapter.ReadLine();
                if (GetStatusRegex().Match(status).Success) {
                    Logger.Info($"Direct connect: confirmed {SystemName} at {host}:{tcpPort}");
                    return adapter;
                }
                adapter.Dispose();
            } catch (Exception ex) {
                Logger.Warning($"Direct connect failed for {host}:{tcpPort} — {ex.Message}");
            }
            return null;
        }

        // -----------------------------------------------------------------------
        // Étape 1 : résolution mDNS
        // -----------------------------------------------------------------------
        private ISerialLike TryMdnsResolve(int tcpPort, out string resolvedIp) {
            resolvedIp = null;
            var hostname = MdnsHostname + ".local";
            Logger.Info($"Trying mDNS resolution: {hostname}");
            try {
                var addresses = Dns.GetHostAddresses(hostname);
                foreach (var addr in addresses) {
                    if (addr.AddressFamily != AddressFamily.InterNetwork) continue;
                    var host = addr.ToString();
                    Logger.Info($"mDNS resolved {hostname} to {host}, probing...");
                    var result = TryDirectConnect(host, tcpPort);
                    if (result != null) {
                        resolvedIp = host;
                        return result;
                    }
                }
            } catch (Exception ex) {
                Logger.Warning($"mDNS resolution failed for {hostname}: {ex.Message}");
            }
            return null;
        }

        // -----------------------------------------------------------------------
        // Étape 2 : scan des hôtes connus dans la table ARP
        // -----------------------------------------------------------------------
        private async Task<ISerialLike> TryArpScanAsync(int tcpPort, CancellationToken token) {
            var arpHosts = GetArpHosts();
            if (arpHosts.Count == 0) {
                Logger.Info("ARP table: no hosts found");
                return null;
            }

            Logger.Info($"ARP table: {arpHosts.Count} hosts to probe on port {tcpPort}");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var tasks = arpHosts.Select(host => TryConnectAsync(host, tcpPort, cts.Token)).ToList();

            while (tasks.Count > 0) {
                var completed = await Task.WhenAny(tasks);
                tasks.Remove(completed);
                try {
                    var result = await completed;
                    if (result != null) {
                        cts.Cancel();
                        return result;
                    }
                } catch (OperationCanceledException) { }
            }

            return null;
        }

        private static List<string> GetArpHosts() {
            var hosts = new List<string>();
            try {
                var psi = new ProcessStartInfo("arp", "-a") {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                // Parse les lignes type "  192.168.1.10     aa-bb-cc-dd-ee-ff     dynamic"
                var regex = new Regex(@"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s+([0-9a-fA-F-]+)\s+dynamic", RegexOptions.IgnoreCase);
                foreach (Match m in regex.Matches(output)) {
                    hosts.Add(m.Groups[1].Value);
                }
            } catch (Exception ex) {
                Logger.Warning($"Failed to read ARP table: {ex.Message}");
            }
            return hosts;
        }

        // -----------------------------------------------------------------------
        // Étape 3 : scan complet de tous les subnets
        // -----------------------------------------------------------------------
        /// <summary>Nombre max de connexions TCP simultanées pendant le scan.</summary>
        protected virtual int AutoScanMaxParallelism => 50;

        private async Task<ISerialLike> ScanAllSubnetsAsync(int tcpPort, CancellationToken token) {
            // Collecter tous les ranges à scanner à partir du masque réel de chaque interface
            var ranges = new List<(uint networkAddr, uint broadcastAddr, string cidr)>();
            var seen = new HashSet<uint>(); // éviter les doublons si plusieurs interfaces sur le même réseau

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses) {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ipBytes = addr.Address.GetAddressBytes();
                    var maskBytes = addr.IPv4Mask.GetAddressBytes();
                    uint ip = (uint)(ipBytes[0] << 24 | ipBytes[1] << 16 | ipBytes[2] << 8 | ipBytes[3]);
                    uint mask = (uint)(maskBytes[0] << 24 | maskBytes[1] << 16 | maskBytes[2] << 8 | maskBytes[3]);
                    uint network = ip & mask;
                    uint broadcast = network | ~mask;

                    if (seen.Contains(network)) continue;
                    seen.Add(network);

                    // Calcul du CIDR pour le log
                    int cidrBits = 0;
                    for (uint m = mask; (m & 0x80000000) != 0; m <<= 1) cidrBits++;
                    var netIp = new IPAddress(new[] {
                        (byte)(network >> 24), (byte)(network >> 16 & 0xFF),
                        (byte)(network >> 8 & 0xFF), (byte)(network & 0xFF)
                    });
                    ranges.Add((network, broadcast, $"{netIp}/{cidrBits}"));
                }
            }

            if (ranges.Count == 0) {
                Logger.Warning("Auto-scan: no active IPv4 interfaces found");
                return null;
            }

            // Construire la liste de toutes les IPs à scanner (exclure network et broadcast)
            var hosts = new List<string>();
            foreach (var (networkAddr, broadcastAddr, cidr) in ranges) {
                Logger.Info($"Auto-scan: will scan {cidr} ({broadcastAddr - networkAddr - 1} hosts) on port {tcpPort}");
                for (uint a = networkAddr + 1; a < broadcastAddr; a++) {
                    hosts.Add($"{a >> 24}.{(a >> 16) & 0xFF}.{(a >> 8) & 0xFF}.{a & 0xFF}");
                }
            }

            Logger.Info($"Auto-scan: {hosts.Count} total hosts to scan");

            // Scanner par batch pour éviter de saturer le réseau
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var semaphore = new SemaphoreSlim(AutoScanMaxParallelism);
            var tasks = new List<Task<ISerialLike>>();

            foreach (var host in hosts) {
                if (cts.Token.IsCancellationRequested) break;
                await semaphore.WaitAsync(cts.Token);
                tasks.Add(Task.Run(async () => {
                    try {
                        return await TryConnectAsync(host, tcpPort, cts.Token);
                    } finally {
                        semaphore.Release();
                    }
                }, cts.Token));
            }

            // Retourner dès qu'on trouve le premier résultat non-null
            while (tasks.Count > 0) {
                var completed = await Task.WhenAny(tasks);
                tasks.Remove(completed);
                try {
                    var result = await completed;
                    if (result != null) {
                        cts.Cancel();
                        return result;
                    }
                } catch (OperationCanceledException) {
                    // Tentative annulée, continuer
                }
            }

            return null;
        }

        private async Task<ISerialLike> TryConnectAsync(string host, int tcpPort, CancellationToken token) {
            TcpClient client = null;
            try {
                client = new TcpClient();
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                connectCts.CancelAfter(AutoScanTimeoutMs);

                await client.ConnectAsync(host, tcpPort, connectCts.Token);
                if (!client.Connected) { client.Dispose(); return null; }

                var stream = client.GetStream();
                stream.ReadTimeout = AutoScanTimeoutMs;
                stream.WriteTimeout = AutoScanTimeoutMs;

                // Envoyer ?
                var query = Encoding.UTF8.GetBytes("?" + NewLineSequence);
                await stream.WriteAsync(query, 0, query.Length, connectCts.Token);

                // Petit délai pour laisser l'ESP32 préparer sa réponse
                await Task.Delay(50, connectCts.Token);

                // Lire la réponse en accumulant jusqu'au timeout ou réponse complète
                var sb = new StringBuilder();
                var buf = new byte[256];
                var deadline = DateTime.UtcNow.AddMilliseconds(AutoScanTimeoutMs);

                while (DateTime.UtcNow < deadline) {
                    if (!stream.DataAvailable) {
                        // Si on a déjà du contenu et qu'il n'y a plus rien, on vérifie
                        if (sb.Length > 0) {
                            await Task.Delay(30, connectCts.Token);
                            if (!stream.DataAvailable) break;
                        } else {
                            await Task.Delay(20, connectCts.Token);
                            continue;
                        }
                    }
                    int read = await stream.ReadAsync(buf, 0, buf.Length, connectCts.Token);
                    if (read == 0) break;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, read));

                    // Vérifier dès qu'on a assez de données
                    if (GetStatusRegex().Match(sb.ToString()).Success) break;
                }

                var response = sb.ToString();
                if (GetStatusRegex().Match(response).Success) {
                    Logger.Info($"Auto-scan found {SystemName} at {host}:{tcpPort}");
                    return new TcpPortAdapter(client, ScanReadTimeout, ScanWriteTimeout, NewLineSequence);
                }

                client.Dispose();
                return null;
            } catch {
                client?.Dispose();
                return null;
            }
        }

        // -----------------------------------------------------------------------
        // État et propriétés
        // -----------------------------------------------------------------------
        public bool Connected => port?.IsOpen == true;
        public string Status { get; private set; }
        public string RemoteEndpoint { get; private set; }

        private float XPosition { get; set; }
        private float YPosition { get; set; }
        private float ZPosition { get; set; }

        public LastDirection XLastDirection { get; private set; } = LastDirection.Positive;
        public LastDirection YLastDirection { get; private set; } = LastDirection.Positive;
        public LastDirection ZLastDirection { get; private set; } = LastDirection.Positive;

        public float XPosition1 => XPosition / XGearRatio;
        public float YPosition1 => YPosition / YGearRatio;
        public float ZPosition1 => ZPosition / ZGearRatio;

        public abstract float XGearRatio { get; set; }
        public abstract float YGearRatio { get; set; }
        public float ZGearRatio { get; set; } = 1;

        private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        // -----------------------------------------------------------------------
        // Mouvements
        // -----------------------------------------------------------------------
        public async Task MoveRelative(Axis axis, int speed, float position, CancellationToken token) {
            await semaphore.WaitAsync(token);
            try {
                UpdateStatus();
                var (axisCmd, gearRatio, getPos, setDir) = GetAxisParams(axis, position >= 0);
                var target = getPos() + position * gearRatio;
                setDir();
                var command = $"$J=G91G21{axisCmd}{(position * gearRatio).ToString(CultureInfo.InvariantCulture)}F{speed.ToString(CultureInfo.InvariantCulture)}";
                Logger.Info($"Sending command: {command}");
                port.WriteLine(command);
                Logger.Info($"Response: {port.ReadLine()}");
                await WaitForTarget(getPos, target, token);
            } finally {
                semaphore.Release();
            }
        }

        public async Task MoveAbsolute(Axis axis, int speed, float position, CancellationToken token) {
            await semaphore.WaitAsync(token);
            try {
                UpdateStatus();
                var (axisCmd, gearRatio, getPos, setDir) = GetAxisParams(axis, position - GetPosition1(axis) >= 0);
                var target = position * gearRatio;
                setDir();
                var command = $"$J=G53{axisCmd}{target.ToString(CultureInfo.InvariantCulture)}F{speed.ToString(CultureInfo.InvariantCulture)}";
                Logger.Info($"Sending command: {command}");
                port.WriteLine(command);
                Logger.Info($"Response: {port.ReadLine()}");
                await WaitForTarget(getPos, target, token);
            } finally {
                semaphore.Release();
            }
        }

        private float GetPosition1(Axis axis) => axis switch {
            Axis.XAxis => XPosition1,
            Axis.YAxis => YPosition1,
            Axis.ZAxis => ZPosition1,
            _ => 0
        };

        private (string cmd, float gear, Func<float> getPos, Action setDir) GetAxisParams(Axis axis, bool positive) {
            return axis switch {
                Axis.XAxis => ("X", XGearRatio, () => XPosition,
                    () => XLastDirection = positive ? LastDirection.Positive : LastDirection.Negative),
                Axis.YAxis => ("Y", YGearRatio, () => YPosition,
                    () => YLastDirection = positive ? LastDirection.Positive : LastDirection.Negative),
                Axis.ZAxis => ("Z", ZGearRatio, () => ZPosition,
                    () => ZLastDirection = positive ? LastDirection.Positive : LastDirection.Negative),
                _ => throw new ArgumentException("Invalid Axis")
            };
        }

        private async Task WaitForTarget(Func<float> getPos, float target, CancellationToken token) {
            var timeout = TimeSpan.FromSeconds(30);
            var startTime = DateTime.Now;
            var lastPos = getPos();
            var stuckCount = 0;

            while (Math.Abs(getPos() - target) > 0.01f) {
                UpdateStatus();
                var currentPos = getPos();
                if (Math.Abs(currentPos - lastPos) < 0.01f) {
                    if (++stuckCount > 5)
                        throw new TimeoutException($"Motor appears stuck at {currentPos}. Target: {target}");
                } else {
                    stuckCount = 0;
                }
                lastPos = currentPos;
                if (DateTime.Now - startTime > timeout)
                    throw new TimeoutException($"Movement timeout. Current: {currentPos}, Target: {target}");
                await Task.Delay(300, token);
            }
        }

        // -----------------------------------------------------------------------
        // Status
        // -----------------------------------------------------------------------
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
            try { UpdateStatus(); }
            finally { semaphore.Release(); }
        }

        public void Dispose() => port?.Dispose();
    }
}
