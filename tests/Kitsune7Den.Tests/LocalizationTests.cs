using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kitsune7Den.Localization;

namespace Kitsune7Den.Tests;

[Collection("Localization")]
public class LocalizationTests : IDisposable
{
    private static readonly string ResourcesDir = Path.Combine(
        FindRepoRoot(), "src", "Kitsune7Den", "Resources");

    public void Dispose() => Loc.Instance.SetLanguage(Loc.English);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kitsune7Den.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }

    private static Dictionary<string, string> ReadResx(string fileName) =>
        XDocument.Load(Path.Combine(ResourcesDir, fileName))
            .Root!.Elements("data")
            .ToDictionary(d => d.Attribute("name")!.Value, d => d.Element("value")?.Value ?? "");

    /// <summary>Every shipped language but English, as xUnit theory data.</summary>
    public static TheoryData<string> Translations()
    {
        var data = new TheoryData<string>();
        foreach (var l in Loc.Languages.Where(l => l.Code != Loc.English))
            data.Add(l.Code);
        return data;
    }

    [Fact]
    public void EveryShippedLanguage_HasAResxFile()
    {
        var files = Directory.GetFiles(ResourcesDir, "Strings.*.resx")
            .Select(f => Path.GetFileName(f)["Strings.".Length..^".resx".Length])
            .Order()
            .ToList();
        Assert.Equal(Translations().Select(d => (string)d[0]).Order(), files);
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void EveryEnglishString_IsTranslated(string code)
    {
        var en = ReadResx("Strings.resx");
        var tr = ReadResx($"Strings.{code}.resx");

        var missing = en.Keys.Where(k => !tr.ContainsKey(k) || string.IsNullOrWhiteSpace(tr[k])).ToList();
        Assert.True(missing.Count == 0, $"Missing {code}: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void TranslationsHaveTheSameKeys(string code)
    {
        // Config field keys (Cfg_*) carry their English in FieldDefinitions, not the resx,
        // so the reference set for those is the zh-Hans file.
        var en = ReadResx("Strings.resx");
        var zh = ReadResx("Strings.zh-Hans.resx");
        var tr = ReadResx($"Strings.{code}.resx");

        var orphans = tr.Keys.Where(k => !en.ContainsKey(k) && !k.StartsWith("Cfg")).ToList();
        Assert.True(orphans.Count == 0, $"{code} keys with no English: " + string.Join(", ", orphans));

        var cfgMissing = zh.Keys.Where(k => k.StartsWith("Cfg") && !tr.ContainsKey(k)).ToList();
        Assert.True(cfgMissing.Count == 0, $"{code} missing config keys: " + string.Join(", ", cfgMissing));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void FormatPlaceholders_MatchEnglish(string code)
    {
        var en = ReadResx("Strings.resx");
        var tr = ReadResx($"Strings.{code}.resx");
        var placeholder = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}");

        var mismatched = en.Keys.Where(tr.ContainsKey).Where(k =>
        {
            var a = placeholder.Matches(en[k]).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            var b = placeholder.Matches(tr[k]).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            return !a.SequenceEqual(b);
        }).ToList();

        Assert.True(mismatched.Count == 0, $"{code} placeholder mismatch: " + string.Join(", ", mismatched));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void EveryFormatString_Formats(string code)
    {
        // A stray brace in a translation only fails when string.Format runs - catch it here.
        // Only keys whose English is a format string; {minutes}-style tokens are replaced by hand.
        var numbered = new Regex(@"\{\d");
        var formatKeys = ReadResx("Strings.resx").Where(e => numbered.IsMatch(e.Value)).Select(e => e.Key);
        Loc.Instance.SetLanguage(code);
        var args = Enumerable.Range(0, 10).Select(i => (object)i).ToArray();
        foreach (var key in formatKeys)
        {
            var ex = Record.Exception(() => Loc.F(key, args));
            Assert.True(ex is null, $"{code} {key}: {ex?.Message}");
        }
    }

    [Fact]
    public void SetLanguage_SwitchesLookups()
    {
        Loc.Instance.SetLanguage(Loc.English);
        Assert.Equal("Dashboard", Loc.T("Nav_Dashboard"));

        Loc.Instance.SetLanguage(Loc.SimplifiedChinese);
        Assert.Equal("仪表盘", Loc.T("Nav_Dashboard"));

        Loc.Instance.SetLanguage("pt-BR");
        Assert.Equal("pt-BR", Loc.Instance.Code);
        Assert.NotEqual("Dashboard", Loc.T("Nav_Dashboard"));
    }

    [Fact]
    public void UnknownKey_ShowsTheKey()
    {
        Assert.Equal("No_Such_Key", Loc.T("No_Such_Key"));
        Assert.Null(Loc.TryGet("No_Such_Key"));
    }

    [Theory]
    [InlineData("", "zh-CN", Loc.SimplifiedChinese)]
    [InlineData("", "zh-SG", Loc.SimplifiedChinese)]
    [InlineData("", "zh-TW", Loc.TraditionalChinese)]
    [InlineData("", "zh-HK", Loc.TraditionalChinese)]
    [InlineData("", "zh-Hant", Loc.TraditionalChinese)]
    [InlineData("", "en-US", Loc.English)]
    [InlineData("", "de-DE", "de")]
    [InlineData("", "de-AT", "de")]
    [InlineData("", "pt-BR", "pt-BR")]
    [InlineData("", "pt-PT", "pt-BR")]
    [InlineData("", "ja-JP", "ja")]
    [InlineData("", "nl-NL", Loc.English)]
    [InlineData("en", "zh-CN", Loc.English)]
    [InlineData("zh-hans", "en-US", Loc.SimplifiedChinese)]
    [InlineData("PT-br", "en-US", "pt-BR")]
    [InlineData("klingon", "en-US", Loc.English)]
    public void Resolve_FollowsPreferenceThenWindows(string preference, string system, string expected)
    {
        Assert.Equal(expected, Loc.Resolve(preference, CultureInfo.GetCultureInfo(system)));
    }
}
