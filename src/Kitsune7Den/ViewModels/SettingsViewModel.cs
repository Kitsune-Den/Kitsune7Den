using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitsune7Den.Localization;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SteamCmdService _steamCmd;
    private readonly ThemeService _themeService;
    private readonly UpdateService _updateService;
    private CancellationTokenSource? _updateCts;

    // Theme
    [ObservableProperty] private string _selectedTheme = "Kitsune";
    public string[] AvailableThemes => ThemeService.AvailableThemes;

    // Language
    [ObservableProperty] private LanguageOption _selectedLanguage;
    public IReadOnlyList<LanguageOption> AvailableLanguages => Loc.Languages;

    // App self-update
    [ObservableProperty] private string _currentAppVersion = "";
    [ObservableProperty] private string _latestAppVersion = "";
    [ObservableProperty] private string _updateStatusMessage = "";
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private bool _isDownloadingUpdate;
    [ObservableProperty] private double _updateProgress;
    private ReleaseInfo? _pendingRelease;

    // SteamCMD
    [ObservableProperty] private string _steamCmdPath = "";
    [ObservableProperty] private bool _steamCmdAvailable;
    [ObservableProperty] private string _serverInstallDirectory = "";
    [ObservableProperty] private string _selectedBranch = "public";
    [ObservableProperty] private bool _autoUpdateOnStart;
    [ObservableProperty] private bool _validateOnUpdate = true;

    // Telnet
    [ObservableProperty] private int _telnetPort = 8081;
    [ObservableProperty] private string _telnetPassword = "";

    // State
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private string _statusMessage = "";

    public ObservableCollection<string> Branches { get; } = new(SteamCmdService.KnownBranches);
    public ObservableCollection<string> UpdateLog { get; } = [];

    public SettingsViewModel(AppSettings settings, SteamCmdService steamCmd, ThemeService themeService, UpdateService updateService,
        TelnetService telnetService)
    {
        _settings = settings;
        _steamCmd = steamCmd;
        _themeService = themeService;
        _updateService = updateService;

        CurrentAppVersion = $"v{_updateService.CurrentVersion}";

        _updateService.StatusChanged += msg =>
        {
            Application.Current.Dispatcher.Invoke(() => UpdateStatusMessage = msg);
        };
        _updateService.ProgressChanged += p =>
        {
            Application.Current.Dispatcher.Invoke(() => UpdateProgress = p);
        };

        // Load current values
        _selectedLanguage = Loc.Languages.First(l => l.Code == Loc.Instance.Code);
        SelectedTheme = settings.Theme;
        SteamCmdPath = settings.SteamCmdPath;
        SteamCmdAvailable = steamCmd.IsSteamCmdAvailable;
        ServerInstallDirectory = settings.ServerInstallDirectory;
        SelectedBranch = settings.ServerBranch;
        AutoUpdateOnStart = settings.AutoUpdateOnStart;
        ValidateOnUpdate = settings.ValidateOnUpdate;
        TelnetPort = settings.TelnetPort;
        TelnetPassword = settings.TelnetPassword;

        // A successful login may have picked up new values from serverconfig.xml
        telnetService.ConnectionStateChanged += connected =>
        {
            if (!connected) return;
            Application.Current.Dispatcher.Invoke(() =>
            {
                TelnetPort = _settings.TelnetPort;
                TelnetPassword = _settings.TelnetPassword;
            });
        };

        // Wire SteamCMD output
        _steamCmd.OutputReceived += line =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateLog.Add(line);
                while (UpdateLog.Count > 500) UpdateLog.RemoveAt(0);
            });
        };

        _steamCmd.OperationCompleted += success =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsUpdating = false;
                StatusMessage = Loc.T(success ? "Settings_OperationSucceeded" : "Settings_OperationFailed");
                SteamCmdAvailable = _steamCmd.IsSteamCmdAvailable;
                SteamCmdPath = _settings.SteamCmdPath;
            });
        };
    }

    partial void OnSelectedThemeChanged(string value)
    {
        _themeService.ApplyTheme(value);
        StatusMessage = Loc.F("Settings_ThemeChanged", value);
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value is null) return;
        Loc.Instance.SetLanguage(value.Code);
        _settings.Language = value.Code;
        _settings.Save();
    }

    [RelayCommand]
    private void Save()
    {
        _settings.SteamCmdPath = SteamCmdPath;
        _settings.ServerInstallDirectory = ServerInstallDirectory;
        _settings.ServerBranch = SelectedBranch;
        _settings.AutoUpdateOnStart = AutoUpdateOnStart;
        _settings.ValidateOnUpdate = ValidateOnUpdate;
        _settings.TelnetPort = TelnetPort;
        _settings.TelnetPassword = TelnetPassword;
        _settings.Save();
        StatusMessage = Loc.T("Settings_Saved");
    }

    [RelayCommand]
    private void BrowseSteamCmd()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = $"SteamCMD|steamcmd.exe|{Loc.T("Settings_FilterAllExecutables")}|*.exe",
            Title = Loc.T("Settings_SelectSteamCmdTitle")
        };

        if (dialog.ShowDialog() == true)
        {
            SteamCmdPath = dialog.FileName;
            SteamCmdAvailable = System.IO.File.Exists(SteamCmdPath);
        }
    }

    [RelayCommand]
    private void BrowseInstallDir()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("Settings_SelectInstallDirTitle")
        };

        if (dialog.ShowDialog() == true)
        {
            ServerInstallDirectory = dialog.FolderName;
        }
    }

    [RelayCommand]
    private async Task DownloadSteamCmd()
    {
        if (string.IsNullOrWhiteSpace(ServerInstallDirectory))
        {
            StatusMessage = Loc.T("Settings_SetInstallDirFirstSteamCmd");
            return;
        }

        IsUpdating = true;
        UpdateLog.Clear();

        var steamDir = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(ServerInstallDirectory) ?? ServerInstallDirectory,
            "SteamCMD");

        await Task.Run(() => _steamCmd.DownloadSteamCmdAsync(steamDir));
        SteamCmdPath = _settings.SteamCmdPath;
        SteamCmdAvailable = _steamCmd.IsSteamCmdAvailable;
        IsUpdating = false;
    }

    [RelayCommand]
    private async Task InstallOrUpdate()
    {
        if (!SteamCmdAvailable)
        {
            StatusMessage = Loc.T("Settings_SteamCmdNotFoundDownloadFirst");
            return;
        }

        if (string.IsNullOrWhiteSpace(ServerInstallDirectory))
        {
            StatusMessage = Loc.T("Settings_SetInstallDirFirst");
            return;
        }

        // Save settings before update so SteamCMD uses latest config
        Save();

        IsUpdating = true;
        UpdateLog.Clear();
        StatusMessage = Loc.T("Settings_Updating");

        _updateCts = new CancellationTokenSource();
        await Task.Run(() => _steamCmd.InstallOrUpdateServerAsync(_updateCts.Token));
    }

    [RelayCommand]
    private void CancelUpdate()
    {
        _updateCts?.Cancel();
        StatusMessage = Loc.T("Settings_Cancelling");
    }

    // --- App self-update ---

    [RelayCommand]
    private async Task CheckAppUpdate()
    {
        IsCheckingUpdate = true;
        UpdateAvailable = false;
        _pendingRelease = await _updateService.CheckForUpdateAsync();
        if (_pendingRelease is not null)
        {
            UpdateAvailable = true;
            LatestAppVersion = _pendingRelease.TagName;
        }
        IsCheckingUpdate = false;
    }

    [RelayCommand]
    private async Task InstallAppUpdate()
    {
        if (_pendingRelease is null) return;

        var result = MessageBox.Show(
            Loc.F("Settings_ConfirmUpdateMessage", _pendingRelease.TagName, _pendingRelease.AssetSizeText),
            Loc.T("Settings_ConfirmUpdateTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        IsDownloadingUpdate = true;
        UpdateProgress = 0;
        await _updateService.DownloadAndApplyAsync(_pendingRelease);
        IsDownloadingUpdate = false;
    }

    [RelayCommand]
    private void ViewRelease()
    {
        if (_pendingRelease is null || string.IsNullOrEmpty(_pendingRelease.HtmlUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_pendingRelease.HtmlUrl)
            {
                UseShellExecute = true
            });
        }
        catch { /* best effort */ }
    }
}
