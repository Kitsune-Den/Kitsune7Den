using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitsune7Den.Localization;
using Kitsune7Den.Services;

namespace Kitsune7Den.ViewModels;

/// <summary>The Dashboard's "Automatic Restarts" card (GitHub issue #4).</summary>
public partial class DashboardViewModel
{
    private RestartService _restartService = null!;
    private DispatcherTimer? _restartStatusTimer;

    [ObservableProperty] private bool _crashRestartEnabled;
    [ObservableProperty] private bool _scheduledRestartEnabled;
    [ObservableProperty] private bool _isDailyMode = true;
    [ObservableProperty] private string _restartTimes = "04:00";
    [ObservableProperty] private int _restartIntervalHours = 6;
    [ObservableProperty] private string _restartWarningMinutes = "10, 5, 1";
    [ObservableProperty] private string _restartWarningMessage = "";
    [ObservableProperty] private string _restartFinalMessage = "";
    [ObservableProperty] private string _restartStatusText = "";
    [ObservableProperty] private string _restartSettingsError = "";
    [ObservableProperty] private bool _restartCountdownActive;
    [ObservableProperty] private bool _restartSettingsDirty;

    public int[] RestartIntervalOptions { get; } = [2, 3, 4, 6, 8, 12, 24];

    public string DefaultWarningMessage => Loc.T("Restart_DefaultWarningMessage");
    public string DefaultFinalMessage => Loc.T("Restart_DefaultFinalMessage");

    private void InitRestarts(RestartService restartService)
    {
        _restartService = restartService;

        CrashRestartEnabled = _settings.CrashRestartEnabled;
        ScheduledRestartEnabled = _settings.ScheduledRestartEnabled;
        IsDailyMode = _settings.ScheduledRestartMode != nameof(RestartScheduleMode.Interval);
        RestartTimes = _settings.ScheduledRestartTimes;
        RestartIntervalHours = _settings.ScheduledRestartIntervalHours;
        RestartWarningMinutes = _settings.RestartWarningMinutes;
        RestartWarningMessage = _settings.RestartWarningMessage;
        RestartFinalMessage = _settings.RestartFinalMessage;
        RestartSettingsDirty = false;

        _restartService.StateChanged += () =>
            Application.Current?.Dispatcher.BeginInvoke(UpdateRestartStatus);
        Loc.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(DefaultWarningMessage));
            OnPropertyChanged(nameof(DefaultFinalMessage));
            UpdateRestartStatus();
        };

        // Keep "in 3h 12m" current
        _restartStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _restartStatusTimer.Tick += (_, _) => UpdateRestartStatus();
        _restartStatusTimer.Start();
        UpdateRestartStatus();
    }

    partial void OnCrashRestartEnabledChanged(bool value) => RestartSettingsDirty = true;
    partial void OnScheduledRestartEnabledChanged(bool value) => RestartSettingsDirty = true;
    partial void OnIsDailyModeChanged(bool value) => RestartSettingsDirty = true;
    partial void OnRestartTimesChanged(string value) => RestartSettingsDirty = true;
    partial void OnRestartIntervalHoursChanged(int value) => RestartSettingsDirty = true;
    partial void OnRestartWarningMinutesChanged(string value) => RestartSettingsDirty = true;
    partial void OnRestartWarningMessageChanged(string value) => RestartSettingsDirty = true;
    partial void OnRestartFinalMessageChanged(string value) => RestartSettingsDirty = true;

    [RelayCommand]
    private void SaveRestartSettings()
    {
        if (ScheduledRestartEnabled && IsDailyMode && RestartSchedule.ParseTimes(RestartTimes) is null)
        {
            RestartSettingsError = Loc.T("Restart_BadTimes");
            return;
        }
        if (RestartSchedule.ParseWarnings(RestartWarningMinutes) is null)
        {
            RestartSettingsError = Loc.T("Restart_BadWarnings");
            return;
        }

        RestartSettingsError = "";
        _settings.CrashRestartEnabled = CrashRestartEnabled;
        _settings.ScheduledRestartEnabled = ScheduledRestartEnabled;
        _settings.ScheduledRestartMode = IsDailyMode
            ? nameof(RestartScheduleMode.Daily)
            : nameof(RestartScheduleMode.Interval);
        _settings.ScheduledRestartTimes = RestartTimes.Trim();
        _settings.ScheduledRestartIntervalHours = RestartIntervalHours;
        _settings.RestartWarningMinutes = RestartWarningMinutes.Trim();
        _settings.RestartWarningMessage = RestartWarningMessage.Trim();
        _settings.RestartFinalMessage = RestartFinalMessage.Trim();
        _settings.Save();

        _restartService.ApplySettings();
        RestartSettingsDirty = false;
        UpdateRestartStatus();
    }

    [RelayCommand]
    private void RestartWithWarnings()
    {
        if (_processService.CurrentState != Models.ServerState.Running) return;
        _restartService.RestartWithWarnings();
    }

    [RelayCommand]
    private void CancelRestart() => _restartService.CancelCountdown();

    private void UpdateRestartStatus()
    {
        RestartCountdownActive = _restartService.CountdownActive;

        if (_restartService.NextRestart is { } next)
        {
            var when = next.Date == DateTime.Today
                ? next.ToString("HH:mm", Loc.Instance.Culture)
                : next.ToString("ddd HH:mm", Loc.Instance.Culture);
            RestartStatusText = Loc.F(RestartCountdownActive ? "Restart_CountdownStatus" : "Restart_NextStatus",
                when, FormatRemaining(next - DateTime.Now));
        }
        else if (_settings.ScheduledRestartEnabled && _processService.CurrentState != Models.ServerState.Running)
        {
            RestartStatusText = Loc.T("Restart_WaitingForServer");
        }
        else
        {
            RestartStatusText = Loc.T(_settings.ScheduledRestartEnabled ? "Restart_NoneScheduled" : "Restart_ScheduleOff");
        }
    }

    private static string FormatRemaining(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return Loc.T("Restart_LessThanMinute");
        var hours = (int)span.TotalHours;
        return hours > 0
            ? Loc.F("Restart_HoursMinutes", hours, span.Minutes)
            : Loc.F("Restart_Minutes", (int)Math.Ceiling(span.TotalMinutes));
    }
}
