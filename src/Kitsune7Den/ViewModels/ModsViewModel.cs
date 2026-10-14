using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitsune7Den.Localization;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.ViewModels;

public partial class ModsViewModel : ObservableObject
{
    private readonly ModManagerService _modManager;

    [ObservableProperty] private ModInfo? _selectedMod;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private int _enabledCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string _enabledSummary = "";

    public ObservableCollection<ModInfo> Mods { get; } = [];

    public ModsViewModel(ModManagerService modManager)
    {
        _modManager = modManager;

        _modManager.StatusChanged += msg =>
        {
            Application.Current.Dispatcher.Invoke(() => StatusMessage = msg);
        };

        UpdateEnabledSummary();
        Loc.LanguageChanged += UpdateEnabledSummary;
    }

    partial void OnEnabledCountChanged(int value) => UpdateEnabledSummary();
    partial void OnTotalCountChanged(int value) => UpdateEnabledSummary();

    private void UpdateEnabledSummary() =>
        EnabledSummary = Loc.F("Mods_EnabledSummary", EnabledCount, TotalCount);

    [RelayCommand]
    private void Refresh()
    {
        var mods = _modManager.GetInstalledMods();
        Mods.Clear();
        foreach (var mod in mods)
            Mods.Add(mod);
        TotalCount = mods.Count;
        EnabledCount = mods.Count(m => m.IsEnabled);
        StatusMessage = Loc.F("Mods_CountStatus", TotalCount, EnabledCount);
    }

    [RelayCommand]
    private void Toggle(ModInfo? mod)
    {
        var target = mod ?? SelectedMod;
        if (target is null) return;
        if (_modManager.ToggleMod(target.FolderName))
        {
            StatusMessage = target.IsEnabled
                ? Loc.F("Mods_DisabledMod", target.DisplayName)
                : Loc.F("Mods_EnabledMod", target.DisplayName);
            Refresh();
        }
    }

    [RelayCommand]
    private void Delete(ModInfo? mod)
    {
        var target = mod ?? SelectedMod;
        if (target is null) return;

        var result = MessageBox.Show(
            Loc.F("Mods_DeleteConfirm", target.DisplayName),
            Loc.T("Mods_DeleteConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            if (_modManager.DeleteMod(target.FolderName))
            {
                StatusMessage = Loc.F("Mods_Deleted", target.DisplayName);
                Refresh();
            }
        }
    }

    [RelayCommand]
    private void InstallFromZip()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = $"{Loc.T("Mods_FilterArchives")}|*.zip|{Loc.T("Mods_FilterAllFiles")}|*.*",
            Title = Loc.T("Mods_SelectZipTitle"),
            Multiselect = true
        };

        if (dialog.ShowDialog() != true) return;

        foreach (var file in dialog.FileNames)
        {
            _modManager.InstallFromZip(file);
        }

        Refresh();
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        try
        {
            var settings = AppSettings.Load();
            var modsDir = System.IO.Path.Combine(settings.ServerDirectory, "Mods");
            if (System.IO.Directory.Exists(modsDir))
                System.Diagnostics.Process.Start("explorer.exe", modsDir);
        }
        catch { /* best effort */ }
    }
}
