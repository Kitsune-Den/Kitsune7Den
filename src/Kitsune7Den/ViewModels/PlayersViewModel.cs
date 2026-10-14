using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitsune7Den.Localization;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.ViewModels;

public partial class PlayersViewModel : ObservableObject
{
    private readonly TelnetService _telnetService;
    private readonly AdminService _adminService;
    private System.Threading.Timer? _refreshTimer;

    [ObservableProperty] private PlayerInfo? _selectedPlayer;
    [ObservableProperty] private string _adminFilePath = "";

    public ObservableCollection<PlayerInfo> Players { get; } = [];

    public PlayersViewModel(TelnetService telnetService, AdminService adminService)
    {
        _telnetService = telnetService;
        _adminService = adminService;
        AdminFilePath = adminService.AdminFilePath ?? Loc.T("Players_AdminFileNotFound");

        // Permission badges are computed from the active language when a card is
        // built, so re-add the same players to re-render them after a switch.
        Loc.LanguageChanged += () => Application.Current?.Dispatcher.Invoke(() =>
        {
            if (adminService.AdminFilePath is null)
                AdminFilePath = Loc.T("Players_AdminFileNotFound");
            var current = Players.ToList();
            Players.Clear();
            foreach (var p in current)
                Players.Add(p);
        });

        _telnetService.ConnectionStateChanged += connected =>
        {
            if (connected)
            {
                _refreshTimer = new System.Threading.Timer(_ => _ = RefreshAsync(), null, 0, 10000);
            }
            else
            {
                _refreshTimer?.Dispose();
                _refreshTimer = null;
                Application.Current.Dispatcher.Invoke(() => Players.Clear());
            }
        };
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var players = await _telnetService.GetPlayersAsync();

        // Enrich with admin info
        foreach (var p in players)
        {
            // Check both platform ID and cross ID against admin list
            var platformIdRaw = p.PlatformId;
            var crossIdRaw = p.CrossId;

            if (!string.IsNullOrEmpty(crossIdRaw) && _adminService.IsAdmin(crossIdRaw))
            {
                p.IsAdmin = true;
                p.PermissionLevel = _adminService.GetPermissionLevel(crossIdRaw);
            }
            else if (!string.IsNullOrEmpty(platformIdRaw) && _adminService.IsAdmin(platformIdRaw))
            {
                p.IsAdmin = true;
                p.PermissionLevel = _adminService.GetPermissionLevel(platformIdRaw);
            }
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            Players.Clear();
            foreach (var p in players)
                Players.Add(p);
        });
    }

    [RelayCommand]
    private async Task Kick()
    {
        if (SelectedPlayer is null) return;
        await _telnetService.SendCommandAsync($"kick {SelectedPlayer.Name}");
        await Task.Delay(1000);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Ban()
    {
        if (SelectedPlayer is null) return;
        var result = MessageBox.Show(
            Loc.F("Players_BanConfirm", SelectedPlayer.Name),
            Loc.T("Players_BanConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        await _telnetService.SendCommandAsync($"ban add {SelectedPlayer.Name} 1 year \"Banned from Kitsune7Den\"");
        await Task.Delay(1000);
        await RefreshAsync();
    }

    [RelayCommand]
    private void GiveAdmin()
    {
        if (SelectedPlayer is null) return;

        // Determine platform and ID
        var (platform, userId) = ResolvePlatformId(SelectedPlayer);
        if (userId is null)
        {
            MessageBox.Show(Loc.T("Players_NoPlatformId"), Loc.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _adminService.SetAdmin(platform, userId, SelectedPlayer.Name, 0);
        SelectedPlayer.IsAdmin = true;
        SelectedPlayer.PermissionLevel = 0;

        // Re-trigger UI update
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void RemoveAdmin()
    {
        if (SelectedPlayer is null) return;

        var result = MessageBox.Show(
            Loc.F("Players_RemoveAdminConfirm", SelectedPlayer.Name),
            Loc.T("Players_ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var (_, userId) = ResolvePlatformId(SelectedPlayer);
        if (userId is null) return;

        _adminService.RemoveAdmin(userId);
        SelectedPlayer.IsAdmin = false;
        SelectedPlayer.PermissionLevel = 1000;

        _ = RefreshAsync();
    }

    private static (string platform, string? userId) ResolvePlatformId(PlayerInfo player)
    {
        // crossid format: "EOS_000285d427524db39c4f115689a064c0"
        if (!string.IsNullOrEmpty(player.CrossId))
        {
            var parts = player.CrossId.Split('_', 2);
            if (parts.Length == 2) return (parts[0], parts[1]);
        }

        // pltfmid format: "Steam_76561198015490744"
        if (!string.IsNullOrEmpty(player.PlatformId))
        {
            var parts = player.PlatformId.Split('_', 2);
            if (parts.Length == 2) return (parts[0], parts[1]);
        }

        return ("", null);
    }
}
