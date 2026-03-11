using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using NINA.Plugins.PolarAlignment.AAPA;

namespace NINA.Plugins.PolarAlignment.AAPA {
    public partial class UniversalPolarAlignmentAAPAVM : UniversalPolarAlignmentBaseVM {
        public UniversalPolarAlignmentAAPAVM(IProfileService profileService) : base(profileService) { }

        protected override string SystemName => "AAPA System";

        protected override IPolarAlignmentSystem CreateSystem() => new UniversalPolarAlignmentAAPA();

        public override bool UsePolarAlignmentSystem {
            get => Properties.Settings.Default.UseAAPAPolarAlignmentSystem;
            set {
                Properties.Settings.Default.UseAAPAPolarAlignmentSystem = value;
                if (value && PolarAlignmentPlugin.UniversalPolarAlignmentVM is { } avalon) {
                    avalon.UsePolarAlignmentSystem = false;
                }
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override bool DoAutomatedAdjustments {
            get => Properties.Settings.Default.DoAutomatedAdjustments;
            set {
                Properties.Settings.Default.DoAutomatedAdjustments = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override double AutomatedAdjustmentSettleTime {
            get => Properties.Settings.Default.AutomatedAdjustmentSettleTime;
            set {
                Properties.Settings.Default.AutomatedAdjustmentSettleTime = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override float XGearRatio {
            get => Properties.Settings.Default.AAPAXGearRatio;
            set {
                if (value < 1) { value = 1; }
                Properties.Settings.Default.AAPAXGearRatio = value;
                if (upa != null) { upa.XGearRatio = value; }
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(PositionX));
            }
        }

        public override int XSpeed {
            get => Properties.Settings.Default.AAPAXSpeed;
            set {
                Properties.Settings.Default.AAPAXSpeed = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override float YGearRatio {
            get => Properties.Settings.Default.AAPAYGearRatio;
            set {
                if (value < 1) { value = 1; }
                Properties.Settings.Default.AAPAYGearRatio = value;
                if (upa != null) { upa.YGearRatio = value; }
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(PositionY));
            }
        }

        public override int YSpeed {
            get => Properties.Settings.Default.AAPAYSpeed;
            set {
                Properties.Settings.Default.AAPAYSpeed = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override bool ReverseAzimuth {
            get => Properties.Settings.Default.AAPAReverseAzimuth;
            set {
                Properties.Settings.Default.AAPAReverseAzimuth = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override bool ReverseAltitude {
            get => Properties.Settings.Default.AAPAReverseAltitude;
            set {
                Properties.Settings.Default.AAPAReverseAltitude = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override float XBacklashCompensation {
            get => Properties.Settings.Default.AAPAXBacklashCompensation;
            set {
                Properties.Settings.Default.AAPAXBacklashCompensation = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int XRunCurrent {
            get => Properties.Settings.Default.AAPAXRunCurrent;
            set {
                Properties.Settings.Default.AAPAXRunCurrent = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                if (upa?.Connected == true && upa is UniversalPolarAlignmentAAPA aapa) {
                    aapa.SetXRunCurrent(value);
                }
            }
        }

        public int YRunCurrent {
            get => Properties.Settings.Default.AAPAYRunCurrent;
            set {
                Properties.Settings.Default.AAPAYRunCurrent = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                if (upa?.Connected == true && upa is UniversalPolarAlignmentAAPA aapa) {
                    aapa.SetYRunCurrent(value);
                }
            }
        }

        public int XHoldPercent {
            get => Properties.Settings.Default.AAPAXHoldPercent;
            set {
                Properties.Settings.Default.AAPAXHoldPercent = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                if (upa?.Connected == true && upa is UniversalPolarAlignmentAAPA aapa) {
                    aapa.SetXHoldPercent(value);
                }
            }
        }

        public int YHoldPercent {
            get => Properties.Settings.Default.AAPAYHoldPercent;
            set {
                Properties.Settings.Default.AAPAYHoldPercent = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                if (upa?.Connected == true && upa is UniversalPolarAlignmentAAPA aapa) {
                    aapa.SetYHoldPercent(value);
                }
            }
        }
    }
}
