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
    public const string TraditionalChinese = "zh-Hant";

    public static Loc Instance { get; } = new();

    private static readonly ResourceManager Strings =
        new("Kitsune7Den.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>
    /// Languages offered in Settings: the 13 the game itself ships. Native names, so
    /// people can find their own.
    /// </summary>
    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(English, "English"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("fr", "Français"),
        new("it", "Italiano"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("pl", "Polski"),
        new("pt-BR", "Português (Brasil)"),
        new("ru", "Русский"),
        new("tr", "Türkçe"),
        new(SimplifiedChinese, "简体中文"),
        new(TraditionalChinese, "繁體中文"),
    ];

    private Loc() { }

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(English);

    /// <summary>The active language code, one of <see cref="Languages"/>.</summary>
    public string Code { get; private set; } = English;

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
    /// the Windows UI language if we ship it, matched on the language alone so de-AT
    /// gets German and pt-PT gets the Brazilian Portuguese we have. Chinese goes by
    /// script: Taiwan, Hong Kong and Macau (or an explicit Hant) get Traditional,
    /// everything else Simplified. Anything we don't ship gets English.
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
        var name = system.Name;
        var language = system.TwoLetterISOLanguageName;

        if (language == "zh")
        {
            string[] traditional = ["zh-Hant", "zh-TW", "zh-HK", "zh-MO"];
            return traditional.Any(t => name.StartsWith(t, StringComparison.OrdinalIgnoreCase))
                ? TraditionalChinese
                : SimplifiedChinese;
        }

        return Languages.FirstOrDefault(l => l.Code.Split('-')[0] == language)?.Code ?? English;
    }

    public void SetLanguage(string? preference)
    {
        var code = Resolve(preference);
        if (code == Code) return;

        Code = code;
        Culture = CultureInfo.GetCultureInfo(code);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
        LanguageChanged?.Invoke();
    }
}

public record LanguageOption(string Code, string NativeName)
{
    public override string ToString() => NativeName;
}
