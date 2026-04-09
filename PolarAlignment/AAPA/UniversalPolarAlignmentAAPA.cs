using NINA.Core.Utility;
using System;
using System.Text.RegularExpressions;

namespace NINA.Plugins.PolarAlignment.AAPA {
    public partial class UniversalPolarAlignmentAAPA : UniversalPolarAlignmentBase {
        protected override string SystemName => "AAPA System";
        protected override string NewLineSequence => "\n";
        protected override int ScanReadTimeout => 2000;
        protected override int ScanWriteTimeout => 1000;
        protected override bool ClearBufferOnConnect => true;
        protected override int PostOpenDelayMs => 100;
        protected override int AutoScanTimeoutMs => 500;
        protected override string MdnsHostname => "AAPA-ESP32";

        protected override string GetLastKnownIp() => Properties.Settings.Default.AAPALastKnownIp;
        protected override void SaveLastKnownIp(string ip) {
            Properties.Settings.Default.AAPALastKnownIp = ip;
            CoreUtil.SaveSettings(Properties.Settings.Default);
        }

        protected override string GetLastKnownComPort() => Properties.Settings.Default.AAPALastKnownComPort;
        protected override void SaveLastKnownComPort(string comPort) {
            Properties.Settings.Default.AAPALastKnownComPort = comPort;
            CoreUtil.SaveSettings(Properties.Settings.Default);
        }

        private float xGearRatio = Properties.Settings.Default.AAPAXGearRatio;
        private float yGearRatio = Properties.Settings.Default.AAPAYGearRatio;

        public override float XGearRatio { get => xGearRatio; set => xGearRatio = value; }
        public override float YGearRatio { get => yGearRatio; set => yGearRatio = value; }

        /// <summary>Serial: auto-scan des ports COM.</summary>
        public UniversalPolarAlignmentAAPA() : base() { }

        /// <summary>TCP manuel: connexion directe host:port.</summary>
        public UniversalPolarAlignmentAAPA(string tcpHost, int tcpPort) : base(tcpHost, tcpPort) { }

        /// <summary>TCP auto-scan: scan parallèle de tous les subnets actifs.</summary>
        public UniversalPolarAlignmentAAPA(int tcpPort) : base(tcpPort) { }

        protected override Regex GetStatusRegex() => StatusRegex();

        public void SetXRunCurrent(int currentMA) {
            try { Port.WriteLine($"XC{currentMA}"); Port.ReadLine(); }
            catch (Exception ex) { Logger.Error($"Failed to set X run current: {ex.Message}"); }
        }

        public void SetYRunCurrent(int currentMA) {
            try { Port.WriteLine($"YC{currentMA}"); Port.ReadLine(); }
            catch (Exception ex) { Logger.Error($"Failed to set Y run current: {ex.Message}"); }
        }

        public void SetXHoldPercent(int percent) {
            try { Port.WriteLine($"XH{percent}"); Port.ReadLine(); }
            catch (Exception ex) { Logger.Error($"Failed to set X hold percent: {ex.Message}"); }
        }

        public void SetYHoldPercent(int percent) {
            try { Port.WriteLine($"YH{percent}"); Port.ReadLine(); }
            catch (Exception ex) { Logger.Error($"Failed to set Y hold percent: {ex.Message}"); }
        }

        [GeneratedRegex(@"<(?<status>\w+)\|MPos:(?<x>[+-]?\d+(\.\d+)?),(?<y>[+-]?\d+(\.\d+)?),(?<z>[+-]?\d+(\.\d+)?)(?:\|T:(?<target>[+-]?\d+),R:(?<running>[01]),E:(?<endstop>[01]),S:(?<speed>[+-]?\d+(\.\d+)?))?\|>")]
        private static partial Regex StatusRegex();
    }
}
