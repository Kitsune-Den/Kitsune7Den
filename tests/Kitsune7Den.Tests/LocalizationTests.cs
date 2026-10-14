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

    [Fact]
    public void EveryEnglishString_HasAChineseTranslation()
    {
        var en = ReadResx("Strings.resx");
        var zh = ReadResx("Strings.zh-Hans.resx");

        var missing = en.Keys.Where(k => !zh.ContainsKey(k) || string.IsNullOrWhiteSpace(zh[k])).ToList();
        Assert.True(missing.Count == 0, "Missing zh-Hans: " + string.Join(", ", missing));
    }

    [Fact]
    public void ChineseHasNoOrphanKeys()
    {
        // Config field keys (Cfg_*) carry their English in FieldDefinitions, not the resx.
        var en = ReadResx("Strings.resx");
        var zh = ReadResx("Strings.zh-Hans.resx");

        var orphans = zh.Keys.Where(k => !en.ContainsKey(k) && !k.StartsWith("Cfg")).ToList();
        Assert.True(orphans.Count == 0, "zh-Hans keys with no English: " + string.Join(", ", orphans));
    }

    [Fact]
    public void FormatPlaceholders_MatchBetweenLanguages()
    {
        var en = ReadResx("Strings.resx");
        var zh = ReadResx("Strings.zh-Hans.resx");
        var placeholder = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}");

        var mismatched = en.Keys.Where(zh.ContainsKey).Where(k =>
        {
            var a = placeholder.Matches(en[k]).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            var b = placeholder.Matches(zh[k]).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            return !a.SequenceEqual(b);
        }).ToList();

        Assert.True(mismatched.Count == 0, "Placeholder mismatch: " + string.Join(", ", mismatched));
    }

    [Fact]
    public void SetLanguage_SwitchesLookups()
    {
        Loc.Instance.SetLanguage(Loc.English);
        Assert.Equal("Dashboard", Loc.T("Nav_Dashboard"));

        Loc.Instance.SetLanguage(Loc.SimplifiedChinese);
        Assert.Equal("仪表盘", Loc.T("Nav_Dashboard"));
    }

    [Fact]
    public void UnknownKey_ShowsTheKey()
    {
        Assert.Equal("No_Such_Key", Loc.T("No_Such_Key"));
        Assert.Null(Loc.TryGet("No_Such_Key"));
    }

    [Theory]
    [InlineData("", "zh-CN", Loc.SimplifiedChinese)]
    [InlineData("", "zh-TW", Loc.SimplifiedChinese)]
    [InlineData("", "en-US", Loc.English)]
    [InlineData("", "de-DE", Loc.English)]
    [InlineData("en", "zh-CN", Loc.English)]
    [InlineData("zh-hans", "en-US", Loc.SimplifiedChinese)]
    [InlineData("klingon", "en-US", Loc.English)]
    public void Resolve_FollowsPreferenceThenWindows(string preference, string system, string expected)
    {
        Assert.Equal(expected, Loc.Resolve(preference, CultureInfo.GetCultureInfo(system)));
    }
}
