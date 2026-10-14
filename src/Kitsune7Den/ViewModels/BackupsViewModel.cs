using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitsune7Den.Localization;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.ViewModels;

public partial class BackupsViewModel : ObservableObject
{
    private readonly BackupService _backupService;
    private readonly AppSettings _settings;

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _saveDirectory = "";
    [ObservableProperty] private string _backupDirectory = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private BackupInfo? _selectedBackup;

    // Schedule
    [ObservableProperty] private bool _scheduledEnabled;
    [ObservableProperty] private string _selectedInterval = "360";
    [ObservableProperty] private int _maxBackups = 20;
    [ObservableProperty] private bool _scheduleRunning;
    [ObservableProperty] private string _nextBackupText = "--";

    public ObservableCollection<BackupInfo> Backups { get; } = [];

    public static readonly ScheduleOption[] IntervalOptions =
    [
        new("30", "Backups_Every30Minutes"),
        new("60", "Backups_EveryHour"),
        new("120", "Backups_Every2Hours"),
        new("240", "Backups_Every4Hours"),
        new("360", "Backups_Every6Hours"),
        new("720", "Backups_Every12Hours"),
        new("1440", "Backups_EveryDay"),
    ];

    public BackupsViewModel(BackupService backupService, AppSettings settings)
    {
        _backupService = backupService;
        _settings = settings;
        BackupDirectory = backupService.BackupDirectory;
        SaveDirectory = backupService.FindSaveDirectory() ?? Loc.T("Backups_SaveNotFound");

        // Load schedule settings
        ScheduledEnabled = settings.ScheduledBackupsEnabled;
        SelectedInterval = settings.BackupIntervalMinutes.ToString();
        MaxBackups = settings.MaxBackups;
        ScheduleRunning = backupService.IsScheduleRunning;

        _backupService.StatusChanged += msg =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = msg;
                UpdateNextBackupText();
            });
        };

        Loc.LanguageChanged += OnLanguageChanged;

        // Auto-start schedule if enabled
        if (ScheduledEnabled)
            StartSchedule();
    }

    /// <summary>
    /// Rebuild the strings that stay on screen: the "not found" save path and each
    /// card's relative age (re-adding the items makes their bindings re-read it).
    /// </summary>
    private void OnLanguageChanged()
    {
        SaveDirectory = _backupService.FindSaveDirectory() ?? Loc.T("Backups_SaveNotFound");
        var items = Backups.ToList();
        Backups.Clear();
        foreach (var b in items)
            Backups.Add(b);
    }

    [RelayCommand]
    private void Refresh()
    {
        var backups = _backupService.GetBackups();
        Backups.Clear();
        foreach (var b in backups)
            Backups.Add(b);
        SaveDirectory = _backupService.FindSaveDirectory() ?? Loc.T("Backups_SaveNotFound");
        UpdateNextBackupText();
        StatusMessage = Loc.F("Backups_FoundCount", backups.Count);
    }

    [RelayCommand]
    private async Task CreateBackup()
    {
        IsBusy = true;
        await _backupService.CreateBackupAsync("manual");
        Refresh();
        IsBusy = false;
    }

    [RelayCommand]
    private async Task Restore()
    {
        if (SelectedBackup is null) return;

        var result = MessageBox.Show(
            Loc.F("Backups_RestoreConfirm", SelectedBackup.FileName),
            Loc.T("Backups_RestoreConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        await _backupService.RestoreBackupAsync(SelectedBackup);
        Refresh();
        IsBusy = false;
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedBackup is null) return;

        var result = MessageBox.Show(
            Loc.F("Backups_DeleteConfirm", SelectedBackup.FileName),
            Loc.T("Backups_DeleteConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _backupService.DeleteBackup(SelectedBackup);
        Refresh();
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", BackupDirectory);
        }
        catch { /* best effort */ }
    }

    [RelayCommand]
    private void SaveSchedule()
    {
        _settings.ScheduledBackupsEnabled = ScheduledEnabled;
        _settings.BackupIntervalMinutes = int.TryParse(SelectedInterval, out var m) ? m : 360;
        _settings.MaxBackups = MaxBackups;
        _settings.Save();

        if (ScheduledEnabled)
            StartSchedule();
        else
            StopSchedule();

        StatusMessage = Loc.T("Backups_ScheduleSaved");
    }

    private void StartSchedule()
    {
        _settings.BackupIntervalMinutes = int.TryParse(SelectedInterval, out var m) ? m : 360;
        _backupService.StartSchedule();
        ScheduleRunning = true;
        UpdateNextBackupText();
    }

    private void StopSchedule()
    {
        _backupService.StopSchedule();
        ScheduleRunning = false;
        NextBackupText = "--";
    }

    private void UpdateNextBackupText()
    {
        if (_backupService.NextScheduledBackup is { } next)
            NextBackupText = next.ToString("HH:mm:ss", Loc.Instance.Culture);
        else
            NextBackupText = "--";
    }
}

/// <summary>
/// An interval choice. <see cref="Value"/> (minutes) is what gets saved; <see cref="Label"/>
/// is looked up from <see cref="LabelKey"/> and re-raised on a language switch so the
/// ComboBox text follows it live.
/// </summary>
public sealed class ScheduleOption : ObservableObject
{
    public ScheduleOption(string value, string labelKey)
    {
        Value = value;
        LabelKey = labelKey;
        Loc.LanguageChanged += () => OnPropertyChanged(nameof(Label));
    }

    public string Value { get; }
    public string LabelKey { get; }
    public string Label => Loc.T(LabelKey);

    public override string ToString() => Label;
}
