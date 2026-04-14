using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private bool _suppressDayNightSync;

    [ObservableProperty] private bool _hasChanges;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isFormView = true;
    [ObservableProperty] private string _rawXml = "";

    // Day/Night calculator — friendly inputs that sync to DayNightLength + DayLightLength.
    // Ported from KitsuneDen's resolveDayNightConfig / reverseDayNightConfig (src/lib/day-night.ts).
    // 7D2D stores DayNightLength (total real-minutes, 10-240) and DayLightLength (in-game daylight hours, 1-23).
    [ObservableProperty] private int _dayMinutes = 45;
    [ObservableProperty] private int _nightMinutes = 15;

    public int DayNightTotal => DayMinutes + NightMinutes;
    public int DayPercent => DayNightTotal > 0 ? (int)Math.Round(DayMinutes * 100.0 / DayNightTotal) : 0;
    public string DayNightRawPreview =>
        $"Replaces DayNightLength ({_dayNightLengthProp?.Value ?? "—"}) and DayLightLength ({_dayLightLengthProp?.Value ?? "—"})";

    private const int MinCycle = 10;
    private const int MaxCycle = 240;
    private const int MinDaylight = 1;
    private const int MaxDaylight = 23;

    public ObservableCollection<ConfigGroup> Groups { get; } = [];
    private List<ServerConfigProperty> _allProperties = [];
    private ServerConfigProperty? _dayNightLengthProp;
    private ServerConfigProperty? _dayLightLengthProp;

    public ConfigViewModel(ConfigService configService)
    {
        _configService = configService;
    }

    [RelayCommand]
    private void Load()
    {
        _allProperties = _configService.LoadConfig();
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

        HasChanges = false;
        IsEmpty = _allProperties.Count == 0;
        StatusMessage = _allProperties.Count == 0
            ? "No serverconfig.xml found — configure a server on the Dashboard"
            : $"Loaded {_allProperties.Count} properties";
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

        StatusMessage = success ? "Saved (backup created)" : "Save failed — check XML validity";
        HasChanges = false;
    }

    [RelayCommand]
    private void Reset()
    {
        Load();
        StatusMessage = "Reset to last saved values";
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
    public string Name { get; }
    public string NameUpper => Name.ToUpperInvariant();
    public List<ServerConfigProperty> Properties { get; }

    public ConfigGroup(string name, List<ServerConfigProperty> properties)
    {
        Name = name;
        Properties = properties;
    }
}
