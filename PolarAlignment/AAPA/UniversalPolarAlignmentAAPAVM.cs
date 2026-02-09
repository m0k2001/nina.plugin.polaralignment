using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static NINA.Plugins.PolarAlignment.AAPA.UniversalPolarAlignmentAAPA;

namespace NINA.Plugins.PolarAlignment.AAPA {
    public partial class UniversalPolarAlignmentAAPAVM : BaseVM {
        private UniversalPolarAlignmentAAPA upa;

        public UniversalPolarAlignmentAAPAVM(IProfileService profileService) : base(profileService) {
            IsNotMoving = true;
        }

        [ObservableProperty]
        private bool connected;

        [ObservableProperty]
        private float positionX;

        [ObservableProperty]
        private float positionY;

        [ObservableProperty]
        private float targetPositionX;

        [ObservableProperty]
        private float targetPositionY;

        public bool UsePolarAlignmentSystem {
            get {
                return Properties.Settings.Default.UseAAPAPolarAlignmentSystem;
            }
            set {
                Properties.Settings.Default.UseAAPAPolarAlignmentSystem = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool DoAutomatedAdjustments {
            get {
                return Properties.Settings.Default.DoAutomatedAdjustments;
            }
            set {
                Properties.Settings.Default.DoAutomatedAdjustments = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AutomatedAdjustmentSettleTime {
            get {
                return Properties.Settings.Default.AutomatedAdjustmentSettleTime;
            }
            set {
                Properties.Settings.Default.AutomatedAdjustmentSettleTime = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public float XGearRatio {
            get {
                return Properties.Settings.Default.AAPAXGearRatio;
            }
            set {
                if (value < 1) { value = 1; }
                Properties.Settings.Default.AAPAXGearRatio = value;
                if (upa != null) {
                    upa.XGearRatio = value;
                }
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(PositionX));
            }
        }

        public int XSpeed {
            get {
                return Properties.Settings.Default.AAPAXSpeed;
            }
            set {
                Properties.Settings.Default.AAPAXSpeed = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public float YGearRatio {
            get {
                return Properties.Settings.Default.AAPAYGearRatio;
            }
            set {
                if(value < 1) { value = 1; }
                Properties.Settings.Default.AAPAYGearRatio = value;
                if (upa != null) {
                    upa.YGearRatio = value;
                }
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(PositionY));
            }
        }

        public int YSpeed {
            get {
                return Properties.Settings.Default.AAPAYSpeed;
            }
            set {
                Properties.Settings.Default.AAPAYSpeed = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool ReverseAzimuth {
            get {
                return Properties.Settings.Default.AAPAReverseAzimuth;
            }
            set {
                Properties.Settings.Default.AAPAReverseAzimuth = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool ReverseAltitude {
            get {
                return Properties.Settings.Default.AAPAReverseAltitude;
            }
            set {
                Properties.Settings.Default.AAPAReverseAltitude = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public float XBacklashCompensation {
            get {
                return Properties.Settings.Default.AAPAXBacklashCompensation;
            }
            set {
                Properties.Settings.Default.AAPAXBacklashCompensation = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int XRunCurrent {
            get {
                return Properties.Settings.Default.AAPAXRunCurrent;
            }
            set {
                Properties.Settings.Default.AAPAXRunCurrent = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                if (upa?.Connected == true) {
                    upa.SetXRunCurrent(value);
                }
            }
        }

        public int YRunCurrent {
            get {
                return Properties.Settings.Default.AAPAYRunCurrent;
            }
            set {
                Properties.Settings.Default.AAPAYRunCurrent = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                if (upa?.Connected == true) {
                    upa.SetYRunCurrent(value);
                }
            }
        }

        public int XHoldPercent {
            get {
                return Properties.Settings.Default.AAPAXHoldPercent;
            }
            set {
                Properties.Settings.Default.AAPAXHoldPercent = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                if (upa?.Connected == true) {
                    upa.SetXHoldPercent(value);
                }
            }
        }

        public int YHoldPercent {
            get {
                return Properties.Settings.Default.AAPAYHoldPercent;
            }
            set {
                Properties.Settings.Default.AAPAYHoldPercent = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                if (upa?.Connected == true) {
                    upa.SetYHoldPercent(value);
                }
            }
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(NudgeXCommand))]
        [NotifyCanExecuteChangedFor(nameof(NudgeYCommand))]
        [NotifyCanExecuteChangedFor(nameof(MoveXCommand))]
        [NotifyCanExecuteChangedFor(nameof(MoveYCommand))]
        private bool isNotMoving;

        private CancellationTokenSource pollCts;

        [RelayCommand]
        public Task Connect() {
            if(upa?.Connected == true) { return Task.CompletedTask; }
            return Task.Run(async () => {
                try {
                    await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = true);

                    upa = new UniversalPolarAlignmentAAPA();
                    _ = StartPoll();
                    Connected = true;
                    Notification.ShowInformation("Successfully connected to AAPA System");
                } catch (Exception ex) {
                    Logger.Error(ex);
                    Notification.ShowError("Unable to connect to AAPA System");
                }
            });
        }

        [RelayCommand]
        public void Disconnect() {
            if (upa?.Connected != true) { return; }
            Connected = false;
            try {
                pollCts?.Cancel();
                upa.Dispose();
            } catch (Exception ex) {
                Logger.Error(ex);
            }
            Notification.ShowInformation("Disconnected from AAPA System");
        }

        [RelayCommand(CanExecute = (nameof(IsNotMoving)))]
        public async Task NudgeX(float position, CancellationToken token) {
            try {
                if (ReverseAzimuth) { position = position * -1; }
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = false);

                Logger.Info($"Nudging AAPA along X axis by {position}");
                var lastDirection = upa.XLastDirection;
                await upa.MoveRelative(UniversalPolarAlignmentAAPA.Axis.XAxis, XSpeed, position, token).ConfigureAwait(false);
                var currentDirection = upa.XLastDirection;
                await ClearBacklash(lastDirection, currentDirection, token);
            } catch (Exception ex) {
                Logger.Error(ex);
                if (ex is TimeoutException) {
                    Notification.ShowError($"Movement timeout: {ex.Message}");
                }
            } finally {
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = true);
            }
        }

        [RelayCommand(CanExecute = (nameof(IsNotMoving)))]
        public async Task NudgeY(float position, CancellationToken token) {
            try {
                if (ReverseAltitude) { position = position * -1; }
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = false);

                Logger.Info($"Nudging AAPA along Y axis by {position}");
                await upa.MoveRelative(UniversalPolarAlignmentAAPA.Axis.YAxis, YSpeed, position, token).ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Error(ex);
                if (ex is TimeoutException) {
                    Notification.ShowError($"Movement timeout: {ex.Message}");
                }
            } finally {
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = true);
            }
        }

        public new void RaiseAllPropertiesChanged() {
            base.RaiseAllPropertiesChanged();
        }

        [RelayCommand(CanExecute = (nameof(IsNotMoving)))]
        public async Task MoveX(CancellationToken token) {
            try {
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = false);

                var target = TargetPositionX;
                if(ReverseAzimuth) { target = target * -1; }

                Logger.Info($"Moving AAPA along X axis to {target}");
                var lastDirection = upa.XLastDirection;

                await upa.MoveAbsolute(UniversalPolarAlignmentAAPA.Axis.XAxis, XSpeed, target, token).ConfigureAwait(false);
                var currentDirection = upa.XLastDirection;
                await ClearBacklash(lastDirection, currentDirection, token);
            } catch (Exception ex) {
                Logger.Error(ex);
                if (ex is TimeoutException) {
                    Notification.ShowError($"Movement timeout: {ex.Message}");
                }
            } finally {
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = true);
            }
        }

        private async Task ClearBacklash(LastDirection lastDirection, LastDirection currentDirection, CancellationToken token) {
            if (lastDirection != currentDirection) {
                if (Math.Abs(XBacklashCompensation) > 0) {
                    Logger.Info("Direction changed. Clearing backlash");
                    await upa.MoveRelative(UniversalPolarAlignmentAAPA.Axis.XAxis, XSpeed, -XBacklashCompensation, token).ConfigureAwait(false);
                    await upa.MoveRelative(UniversalPolarAlignmentAAPA.Axis.XAxis, XSpeed, XBacklashCompensation, token).ConfigureAwait(false);
                }
            }
        }


        [RelayCommand(CanExecute = (nameof(IsNotMoving)))]
        public async Task MoveY(CancellationToken token) {
            try {
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = false);

                var target = TargetPositionY;
                if (ReverseAltitude) { target = target * -1; }

                Logger.Info($"Moving AAPA along Y axis to {target}");
                await upa.MoveAbsolute(UniversalPolarAlignmentAAPA.Axis.YAxis, YSpeed, target, token).ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Error(ex);
                if (ex is TimeoutException) {
                    Notification.ShowError($"Movement timeout: {ex.Message}");
                }
            } finally {
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = true);
            }
        }

        private async Task StartPoll() {
            pollCts = new CancellationTokenSource();
            var token = pollCts.Token;
            var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(300));
            try {
                while (await timer.WaitForNextTickAsync(token) && !token.IsCancellationRequested) {
                    await upa.RefreshStatus(token);
                    PositionX = upa.XPosition1;
                    PositionY = upa.YPosition1;
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
            }
        }
    }
}
