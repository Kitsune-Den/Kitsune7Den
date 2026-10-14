using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitsune7Den.Localization;
using Kitsune7Den.Services.Sandbox;

namespace Kitsune7Den.ViewModels;

/// <summary>
/// Edits a 7D2D 3.x SandboxCode as the game's own Sandbox Options screen would: every
/// option as a dropdown, grouped by the game's tabs, plus the built-in presets. The code
/// is re-encoded on every change, and pasting a code re-reads it.
/// </summary>
public partial class SandboxEditorViewModel : ObservableObject
{
    private readonly SandboxTable _table;
    private readonly List<SandboxOptionViewModel> _options = [];
    private List<SandboxUnknownEntry> _passthrough = [];
    private bool _syncing;

    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _codeProblem = "";
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private SandboxCategoryViewModel? _selectedCategory;
    [ObservableProperty] private SandboxPresetViewModel? _selectedPreset;
    [ObservableProperty] private string _matchingPresetText = "";
    [ObservableProperty] private int _changedCount;

    public string TableNote { get; }
    public bool TableIsUncertain { get; }
    public ObservableCollection<SandboxCategoryViewModel> Categories { get; } = [];
    public List<SandboxPresetViewModel> Presets { get; }

    /// <summary>Raised when the user confirms; the view closes with the code in <see cref="Code"/>.</summary>
    public event Action<bool>? CloseRequested;

    /// <param name="tableNote">Which table is in use and why, shown at the top.</param>
    /// <param name="tableIsUncertain">True when the server's exact version is unknown or newer than the table.</param>
    public SandboxEditorViewModel(SandboxTable table, string? code, string tableNote, bool tableIsUncertain)
    {
        _table = table;
        TableNote = tableNote;
        TableIsUncertain = tableIsUncertain;

        foreach (var category in table.Categories)
        {
            var options = table.Options
                .Where(o => o.Category == category)
                .OrderBy(o => o.Order)
                .Select(o => new SandboxOptionViewModel(table, o, OnOptionChanged))
                .ToList();
            _options.AddRange(options);
            Categories.Add(new SandboxCategoryViewModel(table.CategoryLabel(category), options));
        }

        Presets = table.Presets.Select(p => new SandboxPresetViewModel(table, p)).ToList();

        SelectedCategory = Categories.FirstOrDefault();
        Code = string.IsNullOrWhiteSpace(code) ? table.CodeVersion : code.Trim();
    }

    partial void OnCodeChanged(string value)
    {
        if (_syncing) return;

        var decoded = SandboxCodec.Decode(value, _table);
        if (!decoded.Ok)
        {
            CodeProblem = decoded.Error switch
            {
                SandboxDecodeError.Empty => Loc.T("Sandbox_CodeEmpty"),
                SandboxDecodeError.NotLetters => Loc.T("Sandbox_CodeNotLetters"),
                _ => Loc.F("Sandbox_CodeWrongVersion", decoded.CodeVersion, _table.CodeVersion),
            };
            return;
        }

        _passthrough = decoded.Unknown;
        CodeProblem = decoded.Unknown.Count > 0
            ? Loc.F("Sandbox_CodeUnknownTriples", decoded.Unknown.Count, string.Join(" ", decoded.Unknown.Select(u => u.Raw)))
            : "";

        var selection = SandboxCodec.SelectionFromCode(value, _table);
        _syncing = true;
        try
        {
            foreach (var option in _options)
                option.SelectedIndex = selection[option.Name];
        }
        finally
        {
            _syncing = false;
        }
        RefreshSummary();
    }

    private void OnOptionChanged()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            Code = SandboxCodec.Encode(Selection(), _table, _passthrough);
        }
        finally
        {
            _syncing = false;
        }
        RefreshSummary();
    }

    partial void OnSelectedPresetChanged(SandboxPresetViewModel? value)
    {
        if (value is null || _syncing) return;
        // Applying a preset replaces everything, as in-game; unknown triples go with it.
        _passthrough = [];
        Code = SandboxCodec.Encode(SandboxCodec.SelectionFromPreset(value.Preset, _table), _table);
    }

    partial void OnFilterChanged(string value)
    {
        foreach (var category in Categories)
            category.ApplyFilter(value);

        if (SelectedCategory is null || SelectedCategory.VisibleOptions.Count == 0)
            SelectedCategory = Categories.FirstOrDefault(c => c.VisibleOptions.Count > 0) ?? SelectedCategory;
    }

    private Dictionary<string, int> Selection() =>
        _options.ToDictionary(o => o.Name, o => o.SelectedIndex);

    private void RefreshSummary()
    {
        ChangedCount = _options.Count(o => o.IsChanged);
        foreach (var category in Categories)
            category.RefreshChangedCount();

        var match = SandboxCodec.MatchingPreset(Selection(), _table);
        MatchingPresetText = match is not null
            ? Loc.F("Sandbox_MatchesPreset", _table.PresetLabel(match))
            : Loc.T("Sandbox_CustomRules");

        _syncing = true;
        try
        {
            SelectedPreset = match is null ? null : Presets.FirstOrDefault(p => p.Preset == match);
        }
        finally
        {
            _syncing = false;
        }
    }

    [RelayCommand]
    private void ResetAll()
    {
        _passthrough = [];
        Code = _table.CodeVersion;
    }

    [RelayCommand]
    private void ResetOption(SandboxOptionViewModel? option)
    {
        if (option is not null)
            option.SelectedIndex = option.DefaultIndex;
    }

    [RelayCommand]
    private void Apply()
    {
        if (!string.IsNullOrEmpty(CodeProblem) && !SandboxCodec.Decode(Code, _table).Ok)
            return;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}

public partial class SandboxCategoryViewModel : ObservableObject
{
    private readonly List<SandboxOptionViewModel> _all;

    [ObservableProperty] private int _changedCount;

    public string Label { get; }
    public ObservableCollection<SandboxOptionViewModel> VisibleOptions { get; }

    public SandboxCategoryViewModel(string label, List<SandboxOptionViewModel> options)
    {
        Label = label;
        _all = options;
        VisibleOptions = new ObservableCollection<SandboxOptionViewModel>(options);
    }

    public void ApplyFilter(string filter)
    {
        VisibleOptions.Clear();
        foreach (var o in _all.Where(o => o.Matches(filter)))
            VisibleOptions.Add(o);
    }

    public void RefreshChangedCount() => ChangedCount = _all.Count(o => o.IsChanged);
}

public partial class SandboxOptionViewModel : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged), nameof(SelectedChoice))]
    private int _selectedIndex;

    public string Name { get; }
    public string Label { get; }
    public string Description { get; }
    public int DefaultIndex { get; }
    public string DefaultLabel { get; }
    public List<SandboxChoice> Choices { get; }

    public bool IsChanged => SelectedIndex != DefaultIndex;

    public SandboxChoice? SelectedChoice
    {
        get => Choices.FirstOrDefault(c => c.Index == SelectedIndex);
        set { if (value is not null) SelectedIndex = value.Index; }
    }

    public SandboxOptionViewModel(SandboxTable table, SandboxOptionDef option, Action changed)
    {
        _changed = changed;
        Name = option.Name;
        Label = table.OptionLabel(option);
        Description = table.OptionDescription(option);
        DefaultIndex = option.DefaultIndex;
        DefaultLabel = Loc.F("Sandbox_Default", table.ValueLabel(option, option.DefaultIndex));
        Choices = table.ValuesOf(option)
            .Select((_, i) => new SandboxChoice(i, table.ValueLabel(option, i), i == option.DefaultIndex))
            .ToList();
        _selectedIndex = option.DefaultIndex;
    }

    partial void OnSelectedIndexChanged(int value) => _changed();

    public bool Matches(string filter) =>
        string.IsNullOrWhiteSpace(filter) ||
        Label.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
        Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        Description.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}

public record SandboxChoice(int Index, string Label, bool IsDefault)
{
    public override string ToString() => Label;
}

public class SandboxPresetViewModel
{
    public SandboxPreset Preset { get; }
    public string Label { get; }
    public string Description { get; }
    public string Group { get; }

    public SandboxPresetViewModel(SandboxTable table, SandboxPreset preset)
    {
        Preset = preset;
        Label = table.PresetLabel(preset);
        Description = table.PresetDescription(preset);
        Group = table.PresetGroupLabel(preset);
    }

    public override string ToString() => $"{Group}: {Label}";
}
