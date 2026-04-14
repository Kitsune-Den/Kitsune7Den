using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public SettingsViewModel(AppSettings settings, SteamCmdService steamCmd, ThemeService themeService, UpdateService updateService)
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
        SelectedTheme = settings.Theme;
        SteamCmdPath = settings.SteamCmdPath;
        SteamCmdAvailable = steamCmd.IsSteamCmdAvailable;
        ServerInstallDirectory = settings.ServerInstallDirectory;
        SelectedBranch = settings.ServerBranch;
        AutoUpdateOnStart = settings.AutoUpdateOnStart;
        ValidateOnUpdate = settings.ValidateOnUpdate;
        TelnetPort = settings.TelnetPort;
        TelnetPassword = settings.TelnetPassword;

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
                StatusMessage = success ? "Operation completed successfully" : "Operation failed — check log";
                SteamCmdAvailable = _steamCmd.IsSteamCmdAvailable;
                SteamCmdPath = _settings.SteamCmdPath;
            });
        };
    }

    partial void OnSelectedThemeChanged(string value)
    {
        _themeService.ApplyTheme(value);
        StatusMessage = $"Theme changed to {value}";
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
        StatusMessage = "Settings saved";
    }

    [RelayCommand]
    private void BrowseSteamCmd()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "SteamCMD|steamcmd.exe|All Executables|*.exe",
            Title = "Select steamcmd.exe"
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
            Title = "Select Server Install Directory"
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
            StatusMessage = "Set a server install directory first — SteamCMD will be placed nearby";
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
            StatusMessage = "SteamCMD not found — download or browse to it first";
            return;
        }

        if (string.IsNullOrWhiteSpace(ServerInstallDirectory))
        {
            StatusMessage = "Set a server install directory first";
            return;
        }

        // Save settings before update so SteamCMD uses latest config
        Save();

        IsUpdating = true;
        UpdateLog.Clear();
        StatusMessage = "Updating...";

        _updateCts = new CancellationTokenSource();
        await Task.Run(() => _steamCmd.InstallOrUpdateServerAsync(_updateCts.Token));
    }

    [RelayCommand]
    private void CancelUpdate()
    {
        _updateCts?.Cancel();
        StatusMessage = "Cancelling...";
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
            $"Install update {_pendingRelease.TagName} ({_pendingRelease.AssetSizeText})?\n\n" +
            "Kitsune7Den will download the new version and restart. The server will keep running.",
            "Confirm Update", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
