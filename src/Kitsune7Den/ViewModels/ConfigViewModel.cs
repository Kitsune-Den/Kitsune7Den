using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Kitsune7Den.Localization;
using Kitsune7Den.Models;
using Kitsune7Den.Services;
using Kitsune7Den.Services.Sandbox;
using Kitsune7Den.Views;

namespace Kitsune7Den.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly GameVersionService _gameVersionService;
    private bool _suppressDayNightSync;
    private DetectedGameVersion _detected = DetectedGameVersion.Unknown;

    [ObservableProperty] private bool _hasChanges;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isFormView = true;
    [ObservableProperty] private string _rawXml = "";

    // 7D2D 3.x: gameplay rules moved from serverconfig.xml into SandboxCode
    [ObservableProperty] private bool _isSandboxEra;
    [ObservableProperty] private bool _needsMigration;
    [ObservableProperty] private string _gameVersionText = "";
    [ObservableProperty] private string _sandboxSummary = "";
    [ObservableProperty] private bool _showDayNightCalculator;

    // Day/Night calculator — friendly inputs that sync to DayNightLength + DayLightLength.
    // Ported from KitsuneDen's resolveDayNightConfig / reverseDayNightConfig (src/lib/day-night.ts).
    // 7D2D stores DayNightLength (total real-minutes, 10-240) and DayLightLength (in-game daylight hours, 1-23).
    [ObservableProperty] private int _dayMinutes = 45;
    [ObservableProperty] private int _nightMinutes = 15;

    public int DayNightTotal => DayMinutes + NightMinutes;
    public int DayPercent => DayNightTotal > 0 ? (int)Math.Round(DayMinutes * 100.0 / DayNightTotal) : 0;
    public string DayNightRawPreview =>
        Loc.F("Config_DayNightPreview", _dayNightLengthProp?.Value ?? "—", _dayLightLengthProp?.Value ?? "—");

    private const int MinCycle = 10;
    private const int MaxCycle = 240;
    private const int MinDaylight = 1;
    private const int MaxDaylight = 23;

    public ObservableCollection<ConfigGroup> Groups { get; } = [];
    private List<ServerConfigProperty> _allProperties = [];
    private ServerConfigProperty? _dayNightLengthProp;
    private ServerConfigProperty? _dayLightLengthProp;

    public ConfigViewModel(ConfigService configService, GameVersionService gameVersionService)
    {
        _configService = configService;
        _gameVersionService = gameVersionService;
        Loc.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        foreach (var prop in _allProperties)
            FieldDefinitions.Localize(prop);
        BuildGroups();
        UpdateVersionText();
        UpdateSandboxSummary();
        OnPropertyChanged(nameof(DayNightRawPreview));
        if (StatusMessage.Length > 0 && !HasChanges)
            StatusMessage = LoadedMessage();
    }

    [RelayCommand]
    private void Load()
    {
        foreach (var prop in _allProperties)
            prop.PropertyChanged -= OnPropertyChanged;

        _detected = _gameVersionService.Detect();
        _allProperties = _configService.LoadConfig(_detected.IsSandboxEra);

        // A config that already carries SandboxCode is 3.x-shaped even if we couldn't
        // tell the version from the install (no log yet, "public" branch).
        if (!_detected.IsSandboxEra && _allProperties.Any(p => p.Name == "SandboxCode"))
        {
            _detected = new DetectedGameVersion(null, GameVersionSource.InstallFiles);
            _allProperties = _configService.LoadConfig(sandboxEra: true);
        }

        IsSandboxEra = _detected.IsSandboxEra;
        NeedsMigration = IsSandboxEra && _configService.NeedsSandboxMigration();
        UpdateVersionText();
        BuildGroups();
        RawXml = _configService.GetRawConfig() ?? "";

        // Wire up change tracking
        foreach (var prop in _allProperties)
        {
            prop.PropertyChanged += OnPropertyChanged;
        }

        // Find the two day/night props and seed the calculator from current values
        _dayNightLengthProp = _allProperties.FirstOrDefault(p => p.Name == "DayNightLength");
        _dayLightLengthProp = _allProperties.FirstOrDefault(p => p.Name == "DayLightLength");
        SeedCalculatorFromRaw();
        ShowDayNightCalculator = !IsSandboxEra && _dayNightLengthProp is not null && _dayLightLengthProp is not null;
        UpdateSandboxSummary();

        HasChanges = false;
        IsEmpty = _allProperties.Count == 0;
        StatusMessage = LoadedMessage();
    }

    private string LoadedMessage() => _allProperties.Count == 0
        ? Loc.T("Config_NoConfigFound")
        : Loc.F("Config_Loaded", _allProperties.Count);

    private void UpdateVersionText()
    {
        GameVersionText = _detected switch
        {
            { Version: { } v, Source: GameVersionSource.ServerLog } => Loc.F("Config_VersionFromLog", v),
            { Version: { } v, Source: GameVersionSource.SteamBranch } => Loc.F("Config_VersionFromBranch", v),
            { Source: GameVersionSource.InstallFiles } => Loc.T("Config_VersionThreeX"),
            _ => "",
        };
    }

    // ── Sandbox (3.x) ──

    /// <summary>The option table for this server, and how sure we are it's the right one.</summary>
    private (SandboxTable Table, string Note, bool Uncertain) PickSandboxTable()
    {
        var exact = SandboxTables.For(_detected.Version);
        if (exact is not null)
        {
            return SandboxTables.IsBehind(_detected.Version, exact)
                ? (exact, Loc.F("Sandbox_TableBehind", _detected.Version, exact.Version), true)
                : (exact, Loc.F("Sandbox_TableExact", exact.Version, _detected.Version), false);
        }

        var latest = SandboxTables.Latest;
        return (latest, Loc.F("Sandbox_TableGuess", latest.Version), true);
    }

    private void UpdateSandboxSummary()
    {
        var prop = _allProperties.FirstOrDefault(p => p.Name == "SandboxCode");
        if (prop is null)
        {
            SandboxSummary = "";
            return;
        }

        var (table, _, _) = PickSandboxTable();
        var decoded = SandboxCodec.Decode(prop.Value, table);
        if (!decoded.Ok)
        {
            SandboxSummary = Loc.T("Sandbox_SummaryInvalid");
            return;
        }

        var selection = SandboxCodec.SelectionFromCode(prop.Value, table);
        var preset = SandboxCodec.MatchingPreset(selection, table);
        SandboxSummary = preset is not null
            ? Loc.F("Sandbox_SummaryPreset", table.PresetLabel(preset))
            : Loc.F("Sandbox_SummaryCustom", decoded.Entries.Count(e => e.ValueIndex != e.Option.DefaultIndex));
    }

    [RelayCommand]
    private void OpenSandboxEditor()
    {
        var prop = _allProperties.FirstOrDefault(p => p.Name == "SandboxCode");
        if (prop is null) return;

        var (table, note, uncertain) = PickSandboxTable();
        var editor = new SandboxEditorViewModel(table, prop.Value, note, uncertain);
        var window = new SandboxEditorWindow(editor) { Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true && editor.Code != prop.Value)
        {
            prop.Value = editor.Code;
            UpdateSandboxSummary();
            StatusMessage = Loc.T("Sandbox_Updated");
        }
    }

    [RelayCommand]
    private void MigrateToSandbox()
    {
        if (HasChanges)
        {
            StatusMessage = Loc.T("Config_SaveBeforeMigrate");
            return;
        }

        var confirm = MessageBox.Show(
            Loc.T("Config_MigrateConfirm"), Loc.T("Config_MigrateTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var result = _configService.MigrateToSandbox();
            Load();
            StatusMessage = result.Changed
                ? Loc.F("Config_Migrated", result.CommentedOut.Count, string.Join(", ", result.Added))
                : Loc.T("Config_AlreadyMigrated");
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.F("Config_MigrateFailed", ex.Message);
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        HasChanges = true;
        // If the user edits the raw fields directly, resync the calculator
        if (!_suppressDayNightSync && sender is ServerConfigProperty prop &&
            (prop.Name == "DayNightLength" || prop.Name == "DayLightLength"))
        {
            SeedCalculatorFromRaw();
        }
    }

    private void SeedCalculatorFromRaw()
    {
        if (_dayNightLengthProp is null || _dayLightLengthProp is null) return;
        if (!int.TryParse(_dayNightLengthProp.Value, out var total)) return;
        if (!int.TryParse(_dayLightLengthProp.Value, out var daylightHours)) return;

        total = Math.Clamp(total, MinCycle, MaxCycle);
        daylightHours = Math.Clamp(daylightHours, MinDaylight, MaxDaylight);

        var day = (int)Math.Round((daylightHours / 24.0) * total);
        var night = total - day;
        if (day <= 0) { day = 1; night = total - 1; }
        if (night <= 0) { night = 1; day = total - 1; }

        _suppressDayNightSync = true;
        try
        {
            DayMinutes = day;
            NightMinutes = night;
        }
        finally
        {
            _suppressDayNightSync = false;
        }

        OnPropertyChanged(nameof(DayNightTotal));
        OnPropertyChanged(nameof(DayPercent));
        OnPropertyChanged(nameof(DayNightRawPreview));
    }

    partial void OnDayMinutesChanged(int value) => ApplyDayNightToRaw();
    partial void OnNightMinutesChanged(int value) => ApplyDayNightToRaw();

    private void ApplyDayNightToRaw()
    {
        if (_suppressDayNightSync) return;
        if (_dayNightLengthProp is null || _dayLightLengthProp is null) return;

        var day = Math.Max(1, DayMinutes);
        var night = Math.Max(1, NightMinutes);
        var total = Math.Clamp(day + night, MinCycle, MaxCycle);
        var daylightHours = Math.Clamp(
            (int)Math.Round((day / (double)(day + night)) * 24),
            MinDaylight, MaxDaylight);

        _suppressDayNightSync = true;
        try
        {
            _dayNightLengthProp.Value = total.ToString();
            _dayLightLengthProp.Value = daylightHours.ToString();
        }
        finally
        {
            _suppressDayNightSync = false;
        }

        OnPropertyChanged(nameof(DayNightTotal));
        OnPropertyChanged(nameof(DayPercent));
        OnPropertyChanged(nameof(DayNightRawPreview));
    }

    [RelayCommand]
    private void SetTotalCycle(string? totalText)
    {
        if (!int.TryParse(totalText, out var newTotal)) return;
        newTotal = Math.Clamp(newTotal, MinCycle, MaxCycle);
        var currentTotal = DayNightTotal;
        if (currentTotal <= 0) return;

        var ratio = DayMinutes / (double)currentTotal;
        var newDay = Math.Clamp((int)Math.Round(ratio * newTotal), 1, newTotal - 1);
        DayMinutes = newDay;
        NightMinutes = newTotal - newDay;
    }

    [RelayCommand]
    private void Save()
    {
        bool success;
        if (IsFormView)
        {
            success = _configService.SaveConfig(_allProperties);
        }
        else
        {
            success = _configService.SaveRawConfig(RawXml);
            if (success) Load(); // Reload form from saved XML
        }

        StatusMessage = Loc.T(success ? "Config_Saved" : "Config_SaveFailed");
        HasChanges = false;
    }

    [RelayCommand]
    private void Reset()
    {
        Load();
        StatusMessage = Loc.T("Config_ResetDone");
    }

    [RelayCommand]
    private void SwitchToForm()
    {
        IsFormView = true;
    }

    [RelayCommand]
    private void SwitchToRaw()
    {
        IsFormView = false;
        RawXml = _configService.GetRawConfig() ?? "";
    }

    private void BuildGroups()
    {
        Groups.Clear();

        // Group by category, using the defined order
        var grouped = _allProperties.GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var category in FieldDefinitions.CategoryOrder)
        {
            if (grouped.TryGetValue(category, out var props))
            {
                Groups.Add(new ConfigGroup(category, props));
                grouped.Remove(category);
            }
        }

        // Any remaining "Other" categories
        foreach (var (category, props) in grouped)
        {
            Groups.Add(new ConfigGroup(category, props));
        }
    }
}

public class ConfigGroup
{
    /// <summary>English category key ("Gameplay"), used by the view's triggers.</summary>
    public string Name { get; }
    public string DisplayName { get; }
    public string NameUpper => DisplayName.ToUpper(Loc.Instance.Culture);
    public bool IsLegacy => Name == FieldDefinitions.LegacyCategory;
    public bool IsSandbox => Name == "Sandbox";
    public List<ServerConfigProperty> Properties { get; }

    public ConfigGroup(string name, List<ServerConfigProperty> properties)
    {
        Name = name;
        DisplayName = FieldDefinitions.CategoryLabel(name);
        Properties = properties;
    }
}
