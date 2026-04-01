using System.Text.RegularExpressions;

namespace NINA.Plugins.PolarAlignment.Avalon {
    public partial class UniversalPolarAlignment : UniversalPolarAlignmentBase {
        protected override string SystemName => "Avalon Polar Alignment System";

        private float xGearRatio = Properties.Settings.Default.AvalonXGearRatio;
        private float yGearRatio = Properties.Settings.Default.AvalonYGearRatio;

        public override float XGearRatio { get => xGearRatio; set => xGearRatio = value; }
        public override float YGearRatio { get => yGearRatio; set => yGearRatio = value; }

        /// <summary>Serial: auto-scan des ports COM.</summary>
        public UniversalPolarAlignment() : base() { }

        /// <summary>TCP manuel: connexion directe host:port.</summary>
        public UniversalPolarAlignment(string tcpHost, int tcpPort) : base(tcpHost, tcpPort) { }

        /// <summary>TCP auto-scan: scan parallèle de tous les subnets actifs.</summary>
        public UniversalPolarAlignment(int tcpPort) : base(tcpPort) { }

        protected override Regex GetStatusRegex() => StatusRegex();

        [GeneratedRegex(@"<(?<status>\w+)\|MPos:(?<x>[+-]?\d+(\.\d+)?),(?<y>[+-]?\d+(\.\d+)?),(?<z>[+-]?\d+(\.\d+)?)\|")]
        private static partial Regex StatusRegex();
    }
}
