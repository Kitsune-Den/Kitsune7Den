using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;

namespace Kitsune7Den.Localization;

/// <summary>
/// UI strings, looked up by key from Resources/Strings*.resx.
///
/// XAML binds through <see cref="TrExtension"/> (<c>{l:Tr Nav_Dashboard}</c>), which
/// goes through this singleton's indexer, so switching language re-renders every
/// bound string live — the same way themes swap without a restart. Code uses
/// <see cref="T"/> / <see cref="F"/>. A key missing from a translation falls back to
/// English (the neutral resx), and a key missing everywhere shows the key itself so
/// it's obvious in the UI rather than blank.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string English = "en";
    public const string SimplifiedChinese = "zh-Hans";

    public static Loc Instance { get; } = new();

    private static readonly ResourceManager Strings =
        new("Kitsune7Den.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>Languages offered in Settings. Native names, so people can find their own.</summary>
    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(English, "English"),
        new(SimplifiedChinese, "简体中文"),
    ];

    private Loc() { }

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(English);

    /// <summary>The active language code, one of <see cref="Languages"/>.</summary>
    public string Code { get; private set; } = English;

    public bool IsChinese => Code == SimplifiedChinese;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changes, for view models that hold built strings.</summary>
    public static event Action? LanguageChanged;

    public string this[string key] => Strings.GetString(key, Culture) ?? key;

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args) =>
        string.Format(Instance.Culture, T(key), args);

    /// <summary>The translation for a key, or null if no resx has it (callers supply their own fallback).</summary>
    public static string? TryGet(string key) => Strings.GetString(key, Instance.Culture);

    /// <summary>
    /// Map a saved preference to a supported language. Blank means "follow Windows":
    /// any Chinese UI culture gets Simplified Chinese (the only Chinese we ship yet),
    /// everything else English.
    /// </summary>
    public static string Resolve(string? preference, CultureInfo? systemCulture = null)
    {
        if (!string.IsNullOrWhiteSpace(preference))
        {
            var match = Languages.FirstOrDefault(l =>
                string.Equals(l.Code, preference, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Code;
        }

        var system = systemCulture ?? CultureInfo.CurrentUICulture;
        return system.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? SimplifiedChinese
            : English;
    }

    public void SetLanguage(string? preference)
    {
        var code = Resolve(preference);
        if (code == Code) return;

        Code = code;
        Culture = CultureInfo.GetCultureInfo(code);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChinese)));
        LanguageChanged?.Invoke();
    }
}

public record LanguageOption(string Code, string NativeName)
{
    public override string ToString() => NativeName;
}
