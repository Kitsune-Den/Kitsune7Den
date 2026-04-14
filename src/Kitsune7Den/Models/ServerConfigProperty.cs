using CommunityToolkit.Mvvm.ComponentModel;

namespace Kitsune7Den.Models;

public partial class ServerConfigProperty : ObservableObject
{
    public string Name { get; set; } = string.Empty;

    [ObservableProperty] private string _value = string.Empty;

    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public ConfigFieldType FieldType { get; set; } = ConfigFieldType.Text;
    public string[]? Options { get; set; }
    public string[]? OptionLabels { get; set; }

    /// <summary>
    /// Human-friendly display name derived from the property name.
    /// "ServerMaxPlayerCount" -> "Server Max Player Count"
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(Name)) return Name;
            var chars = new System.Text.StringBuilder();
            for (var i = 0; i < Name.Length; i++)
            {
                if (i > 0 && char.IsUpper(Name[i]) && !char.IsUpper(Name[i - 1]))
                    chars.Append(' ');
                else if (i > 1 && char.IsUpper(Name[i]) && char.IsUpper(Name[i - 1]) &&
                         i + 1 < Name.Length && !char.IsUpper(Name[i + 1]))
                    chars.Append(' ');
                chars.Append(Name[i]);
            }
            return chars.ToString();
        }
    }

    /// <summary>
    /// For select fields: pairs of (value, label) for the ComboBox.
    /// </summary>
    public SelectOption[] SelectOptions
    {
        get
        {
            if (Options is null) return [];
            var labels = OptionLabels ?? Options;
            var result = new SelectOption[Options.Length];
            for (var i = 0; i < Options.Length; i++)
                result[i] = new SelectOption(Options[i], labels.Length > i ? labels[i] : Options[i]);
            return result;
        }
    }

    /// <summary>
    /// For select fields: the currently selected option (by value).
    /// </summary>
    public SelectOption? SelectedOption
    {
        get => System.Array.Find(SelectOptions, o => o.Value == Value);
        set
        {
            if (value is not null)
                Value = value.Value;
        }
    }
}

public record SelectOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public enum ConfigFieldType
{
    Text,
    Number,
    Boolean,
    Password,
    Select,
    EditableSelect
}
