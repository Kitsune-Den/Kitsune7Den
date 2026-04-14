using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ServerProcessService _processService;
    private readonly TelnetService _telnetService;
    private readonly LogWatcherService _logWatcher;
    private readonly AppSettings _settings;
    private System.Threading.Timer? _uptimeTimer;

    [ObservableProperty] private string _serverState = "Stopped";
    [ObservableProperty] private string _uptimeText = "--:--:--";
    [ObservableProperty] private int _playerCount;
    [ObservableProperty] private bool _canStart = true;
    [ObservableProperty] private bool _canStop;
    [ObservableProperty] private string _serverExePath = "";
    [ObservableProperty] private string _serverDirectory = "";

    public bool IsFirstRun => string.IsNullOrWhiteSpace(ServerExePath) || !System.IO.File.Exists(ServerExePath);
    public bool IsConfigured => !IsFirstRun;

    partial void OnServerExePathChanged(string value)
    {
        OnPropertyChanged(nameof(IsFirstRun));
        OnPropertyChanged(nameof(IsConfigured));
    }
    [ObservableProperty] private string _lanIp = "...";
    [ObservableProperty] private string _publicIp = "...";
    [ObservableProperty] private int _serverPort = 26900;

    public DashboardViewModel(ServerProcessService processService, TelnetService telnetService,
        LogWatcherService logWatcher, AppSettings settings)
    {
        _processService = processService;
        _telnetService = telnetService;
        _logWatcher = logWatcher;
        _settings = settings;

        ServerExePath = settings.ServerExePath;
        ServerDirectory = settings.ServerDirectory;

        // Detect IPs on load
        _ = DetectIpsAsync();

        _processService.StateChanged += state =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ServerState = state.ToString();
                CanStart = state == Models.ServerState.Stopped;
                CanStop = state == Models.ServerState.Running;

                if (state == Models.ServerState.Running)
                {
                    _uptimeTimer = new System.Threading.Timer(_ => UpdateUptime(), null, 0, 1000);

                    // Start tailing the server log file
                    if (_processService.CurrentLogFile is not null)
                        _logWatcher.StartWatchingFile(_processService.CurrentLogFile);
                    else if (!string.IsNullOrEmpty(_settings.ServerDirectory))
                        _logWatcher.StartWatching(_settings.ServerDirectory);
                }
                else if (state == Models.ServerState.Stopped)
                {
                    _uptimeTimer?.Dispose();
                    _uptimeTimer = null;
                    _logWatcher.Stop();
                    UptimeText = "--:--:--";
                    PlayerCount = 0;
                }
            });
        };
    }

    [RelayCommand]
    private async Task Start()
    {
        SavePaths();
        await _processService.StartAsync();

        // Auto-connect telnet after a delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(15000); // wait for server to boot
            await _telnetService.ConnectAsync();
        });
    }

    [RelayCommand]
    private async Task ConnectTelnet()
    {
        await _telnetService.ConnectAsync();
    }

    [RelayCommand]
    private async Task Stop()
    {
        await _processService.StopAsync(_telnetService);
        _telnetService.Disconnect();
    }

    [RelayCommand]
    private async Task Restart()
    {
        await _processService.RestartAsync(_telnetService);

        _ = Task.Run(async () =>
        {
            await Task.Delay(15000);
            await _telnetService.ConnectAsync();
        });
    }

    [RelayCommand]
    private void BrowseExe()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Server Executable|7DaysToDieServer.exe|All Executables|*.exe",
            Title = "Select 7 Days to Die Server Executable"
        };

        if (dialog.ShowDialog() == true)
        {
            ServerExePath = dialog.FileName;
            ServerDirectory = System.IO.Path.GetDirectoryName(dialog.FileName) ?? "";
            SavePaths();
            AutoDetectTelnetSettings();
            _ = DetectIpsAsync(); // refresh IP + port with the new config
        }
    }

    /// <summary>
    /// Read telnet port + password from the server's serverconfig.xml so the
    /// user doesn't have to manually enter them in Settings.
    /// </summary>
    private void AutoDetectTelnetSettings()
    {
        try
        {
            var configPath = System.IO.Path.Combine(ServerDirectory, "serverconfig.xml");
            if (!System.IO.File.Exists(configPath)) return;

            var doc = System.Xml.Linq.XDocument.Load(configPath);
            var props = doc.Descendants("property").ToList();

            var portProp = props.FirstOrDefault(p => p.Attribute("name")?.Value == "TelnetPort");
            var pwProp = props.FirstOrDefault(p => p.Attribute("name")?.Value == "TelnetPassword");

            if (portProp is not null && int.TryParse(portProp.Attribute("value")?.Value, out var port))
                _settings.TelnetPort = port;
            if (pwProp is not null)
                _settings.TelnetPassword = pwProp.Attribute("value")?.Value ?? "";

            _settings.Save();
        }
        catch { /* best effort */ }
    }

    private void SavePaths()
    {
        _settings.ServerExePath = ServerExePath;
        _settings.ServerDirectory = ServerDirectory;
        _settings.Save();
    }

    [RelayCommand]
    private void CopyLan()
    {
        Clipboard.SetText($"{LanIp}:{ServerPort}");
    }

    [RelayCommand]
    private void CopyPublic()
    {
        Clipboard.SetText($"{PublicIp}:{ServerPort}");
    }

    private void UpdateUptime()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            UptimeText = _processService.Uptime.ToString(@"hh\:mm\:ss");
        });
    }

    private async Task DetectIpsAsync()
    {
        // Read server port from config
        var configService = new Services.ConfigService(_settings);
        var props = configService.LoadConfig();
        var portProp = props.FirstOrDefault(p => p.Name == "ServerPort");
        if (portProp is not null && int.TryParse(portProp.Value, out var port))
            ServerPort = port;

        // LAN IP — find the first non-loopback, non-link-local IPv4 address
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            // Prefer 192.168.x.x, then 10.x.x.x, then anything non-link-local
            var candidates = host.AddressList
                .Where(a =>
                    a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !System.Net.IPAddress.IsLoopback(a) &&
                    !a.ToString().StartsWith("169.254"))
                .ToList();
            var lanIp = candidates.FirstOrDefault(a => a.ToString().StartsWith("192.168."))
                        ?? candidates.FirstOrDefault(a => a.ToString().StartsWith("10."))
                        ?? candidates.FirstOrDefault();
            LanIp = lanIp?.ToString() ?? "Unknown";
        }
        catch
        {
            LanIp = "Unknown";
        }

        // Public IP
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            PublicIp = (await http.GetStringAsync("https://api.ipify.org")).Trim();
        }
        catch
        {
            PublicIp = "Unavailable";
        }
    }
}
