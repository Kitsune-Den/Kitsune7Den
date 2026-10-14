using System.IO;
using System.Text.RegularExpressions;
using Kitsune7Den.Localization;
using Kitsune7Den.Services;
using Kitsune7Den.Services.Sandbox;

namespace Kitsune7Den.Tests;

/// <summary>
/// SandboxCode codec and the bundled 3.x option tables. The ground truth is a boot log
/// captured from a live V 3.3.0 (b18) dedicated server listing what a code applied.
/// </summary>
[Collection("Localization")]
public class SandboxCodecTests : IDisposable
{
    private static readonly SandboxTable Table33 = SandboxTables.ByVersion("3.3")!;

    public void Dispose() => Loc.Instance.SetLanguage(Loc.English);

    [Fact]
    public void BundlesAllFourTables_OldestFirst()
    {
        Assert.Equal(["3.0", "3.1", "3.2", "3.3"], SandboxTables.All.Select(t => t.Version));
        Assert.Same(Table33, SandboxTables.Latest);
        Assert.All(SandboxTables.All, t => Assert.Equal("A", t.CodeVersion));
        Assert.Equal(165, Table33.Options.Count);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    public void IndexToAlpha_RoundTrips(int index, string letter)
    {
        Assert.Equal(letter, SandboxCodec.IndexToAlpha(index));
        Assert.Equal(index, SandboxCodec.AlphaToIndex(letter[0]));
        Assert.Equal(index, SandboxCodec.AlphaToIndex(char.ToLowerInvariant(letter[0])));
    }

    [Theory]
    [InlineData(0, "AA")]
    [InlineData(1, "AB")]
    [InlineData(26, "BA")]
    [InlineData(164, "GI")]
    [InlineData(675, "ZZ")]
    public void IndexToAlpha2_RoundTrips(int index, string letters)
    {
        Assert.Equal(letters, SandboxCodec.IndexToAlpha2(index));
        Assert.Equal(index, SandboxCodec.Alpha2ToIndex(letters));
    }

    [Fact]
    public void Letters_OutOfRange_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SandboxCodec.IndexToAlpha(26));
        Assert.Throws<ArgumentOutOfRangeException>(() => SandboxCodec.IndexToAlpha2(676));
        Assert.Equal(-1, SandboxCodec.AlphaToIndex('1'));
    }

    [Fact]
    public void Decode_MatchesWhatALive33ServerApplied()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sandbox-3.3-applied.txt"));
        var code = lines.First(l => l.StartsWith("# code: "))["# code: ".Length..].Trim();
        var applied = lines
            .Select(l => Regex.Match(l, @"^Option (\w+): (\d+)/"))
            .Where(m => m.Success)
            .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));

        var decoded = SandboxCodec.Decode(code, Table33);

        Assert.True(decoded.Ok);
        Assert.Empty(decoded.Unknown);
        Assert.Equal("", decoded.Trailing);
        Assert.Equal(applied, decoded.Entries.ToDictionary(e => e.Option.Name, e => e.ValueIndex));
    }

    [Fact]
    public void Encode_ReproducesTheGameCode()
    {
        // The fixture code was written in the game's own order, so re-encoding is byte-identical.
        const string code = "AAAAABMASKBBGBIEBMDBWABZICAGCOHCZDCXFDXIDWBFGGFACEOAFRB";
        var selection = SandboxCodec.SelectionFromCode(code, Table33);
        Assert.Equal(code, SandboxCodec.Encode(selection, Table33));
    }

    [Fact]
    public void EveryBuiltInPreset_DecodesCleanly_InEveryTable()
    {
        // 3.0's table was extracted without presets (no bundle to read them from)
        Assert.NotEmpty(Table33.Presets);
        foreach (var table in SandboxTables.All)
        {
            foreach (var preset in table.Presets.Where(p => p.Code != ""))
            {
                var decoded = SandboxCodec.Decode(preset.Code, table);
                Assert.True(decoded.Ok, $"{table.Version} {preset.Name}");
                Assert.Empty(decoded.Unknown);
                Assert.Equal("", decoded.Trailing);
            }
        }
    }

    [Fact]
    public void DefaultPreset_IsAdventurer()
    {
        var preset = SandboxCodec.MatchingPreset(SandboxCodec.SelectionFromCode("AAAJABJARFBNC", Table33), Table33);
        Assert.NotNull(preset);
        Assert.Equal("Adventurer", preset.Name);
        Assert.True(preset.IsDefault);
    }

    [Fact]
    public void TemplateCode_IsAdventurerPlusBlockAndTerrainDamage()
    {
        // The stock 3.3 serverconfig.xml says its SandboxCode is "the default difficulty of
        // Adventurer", but it also sets Block and Terrain Damage to 150%, so it is not the
        // preset exactly. It must still decode cleanly: migration writes it.
        var decoded = SandboxCodec.Decode("AAAJABJACJADJARFBNC", Table33);
        Assert.True(decoded.Ok);
        Assert.Empty(decoded.Unknown);
        Assert.Contains(decoded.Entries, e => e.Option.Name == "BlockDamage" && Table33.ValueLabel(e.Option, e.ValueIndex) == "150%");
        Assert.Null(SandboxCodec.MatchingPreset(SandboxCodec.SelectionFromCode("AAAJABJACJADJARFBNC", Table33), Table33));
    }

    [Fact]
    public void AllDefaults_EncodeToJustTheVersionLetter()
    {
        Assert.Equal("A", SandboxCodec.Encode(SandboxCodec.Defaults(Table33), Table33));
        var nomad = Table33.Presets.Single(p => p.Name == "Nomad");
        Assert.Equal(nomad, SandboxCodec.MatchingPreset(SandboxCodec.Defaults(Table33), Table33));
    }

    [Fact]
    public void Encode_WritesInGameOrder_RegardlessOfInputOrder()
    {
        var selection = SandboxCodec.Defaults(Table33);
        selection["SillyBigHeads"] = 1;   // late in registration order
        selection["RangedDamage"] = 0;    // first
        Assert.Equal("AAAA" + SandboxCodec.IndexToAlpha2(Table33.OptionByName("SillyBigHeads")!.Index) + "B",
            SandboxCodec.Encode(selection, Table33));
    }

    [Fact]
    public void Encode_RejectsAValueTheOptionDoesNotHave()
    {
        var selection = SandboxCodec.Defaults(Table33);
        selection["SillyBigHeads"] = 9;
        Assert.Throws<ArgumentOutOfRangeException>(() => SandboxCodec.Encode(selection, Table33));
    }

    [Fact]
    public void Decode_KeepsTriplesItCannotPlace_AndEncodePassesThemThrough()
    {
        // ZZ is not an option in 3.3; AAZ is a value out of RangedDamage's range
        var decoded = SandboxCodec.Decode("AZZBAAZAAA", Table33);

        Assert.True(decoded.Ok);
        Assert.Equal(["ZZB", "AAZ"], decoded.Unknown.Select(u => u.Raw));
        Assert.Equal(["unknown-option", "value-out-of-range"], decoded.Unknown.Select(u => u.Reason));
        Assert.Single(decoded.Entries);

        var selection = SandboxCodec.SelectionFromCode("AZZBAAZAAA", Table33);
        Assert.Equal("AAAAZZBAAZ", SandboxCodec.Encode(selection, Table33, decoded.Unknown));
    }

    [Fact]
    public void Decode_RepeatedOption_KeepsTheFirst()
    {
        var decoded = SandboxCodec.Decode("AAAAAAB", Table33);
        Assert.Single(decoded.Entries);
        Assert.Equal(0, decoded.Entries[0].ValueIndex);
    }

    [Theory]
    [InlineData("", SandboxDecodeError.Empty)]
    [InlineData("   ", SandboxDecodeError.Empty)]
    [InlineData("AAA1", SandboxDecodeError.NotLetters)]
    [InlineData("BAAA", SandboxDecodeError.WrongVersion)]
    public void Decode_Malformed(string code, SandboxDecodeError error)
    {
        var decoded = SandboxCodec.Decode(code, Table33);
        Assert.False(decoded.Ok);
        Assert.Equal(error, decoded.Error);
    }

    [Fact]
    public void Decode_IsCaseInsensitive_AndReportsTrailing()
    {
        var decoded = SandboxCodec.Decode("aaaaAB", Table33);
        Assert.True(decoded.Ok);
        Assert.Single(decoded.Entries);
        Assert.Equal("AB", decoded.Trailing);
    }

    [Theory]
    [InlineData("3.0", "3.0", false)]
    [InlineData("3.2", "3.2", false)]
    [InlineData("3.3", "3.3", false)]
    [InlineData("3.5", "3.3", true)]
    [InlineData("4.0", "3.3", true)]
    public void TableFor_PicksExactOrNewestNotAhead(string version, string expected, bool behind)
    {
        var v = GameVersion.Parse(version);
        var table = SandboxTables.For(v)!;
        Assert.Equal(expected, table.Version);
        Assert.Equal(behind, SandboxTables.IsBehind(v, table));
    }

    [Fact]
    public void TableFor_PreSandboxOrUnknown_IsNull()
    {
        Assert.Null(SandboxTables.For(GameVersion.Parse("2.6")));
        Assert.Null(SandboxTables.For(null));
    }

    [Fact]
    public void Item_tier_shift_between_31_and_32_is_real()
    {
        // 3.2 prepended "Default" to the item-tier lists under the same version letter;
        // the per-version tables are what keep "LootMaxTier" letters honest.
        var t31 = SandboxTables.ByVersion("3.1")!;
        var t32 = SandboxTables.ByVersion("3.2")!;
        var o31 = t31.OptionByName("LootMaxTier")!;
        var o32 = t32.OptionByName("LootMaxTier")!;
        Assert.Equal(t31.ValuesOf(o31).Count + 1, t32.ValuesOf(o32).Count);
    }

    [Fact]
    public void Labels_FollowTheUiLanguage()
    {
        var option = Table33.OptionByName("ZombieMove")!;

        Loc.Instance.SetLanguage(Loc.English);
        Assert.Equal("Walk", Table33.ValueLabel(option, 0));
        Assert.Equal("Player", Table33.CategoryLabel("General"));

        Loc.Instance.SetLanguage(Loc.SimplifiedChinese);
        Assert.Equal("行走", Table33.ValueLabel(option, 0));
        Assert.Equal("玩家", Table33.CategoryLabel("General"));
        Assert.Equal("冒险家", Table33.PresetLabel(Table33.Presets.Single(p => p.Name == "Adventurer")));
    }
}
