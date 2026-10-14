using System.IO;
using System.Text.Json;
using Kitsune7Den.Localization;

namespace Kitsune7Den.Services.Sandbox;

/// <summary>
/// One 7D2D version's Sandbox option table: which option each two-letter index is,
/// and which value each third letter selects. Loaded from Resources/Sandbox/sandbox-x.y.json.
///
/// The JSON files are generated from the game assembly by PackRelay's
/// scripts/extract-sandbox-options.mjs (see PackRelay docs/7D2D-SANDBOX-CODES.md) and
/// copied here verbatim. Never hand-edit or reorder them: the array order IS the wire
/// format of every SandboxCode.
/// </summary>
public sealed class SandboxTable
{
    /// <summary>Game minor version this table was extracted from ("3.3").</summary>
    public string Version { get; init; } = "";
    public string GameVersion { get; init; } = "";
    /// <summary>The code's leading letter for this version ("A").</summary>
    public string CodeVersion { get; init; } = "A";
    public List<string> Categories { get; init; } = [];
    public Dictionary<string, string> CategoryLabels { get; init; } = [];
    public Dictionary<string, SandboxValueSet> ValueSets { get; init; } = [];
    public List<SandboxOptionDef> Options { get; init; } = [];
    public List<SandboxPreset> Presets { get; init; } = [];

    /// <summary>zh-Hans strings for this table, keyed like PackRelay's games.json (may be null).</summary>
    internal JsonElement? Chinese { get; set; }

    private Dictionary<int, SandboxOptionDef>? _byIndex;
    private Dictionary<string, SandboxOptionDef>? _byName;

    public SandboxOptionDef? OptionByIndex(int index) =>
        (_byIndex ??= Options.ToDictionary(o => o.Index)).GetValueOrDefault(index);

    public SandboxOptionDef? OptionByName(string name) =>
        (_byName ??= Options.ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase)).GetValueOrDefault(name);

    public IReadOnlyList<SandboxValue> ValuesOf(SandboxOptionDef option) =>
        ValueSets.TryGetValue(option.ValueSet, out var set) ? set.Values : [];

    // ── Display text, in the active UI language ──
    // Chinese comes from the game's own Localization.csv (via PackRelay), so it
    // matches what players see in-game. Anything untranslated falls back to English.

    public string CategoryLabel(string category) =>
        Zh("categories", category) ?? CategoryLabels.GetValueOrDefault(category) ?? category;

    public string OptionLabel(SandboxOptionDef option) =>
        Zh("options", option.Name, "label") ?? option.Label;

    public string OptionDescription(SandboxOptionDef option) =>
        (Zh("options", option.Name, "description") ?? option.Description).Replace("\\n", "\n");

    public string ValueLabel(SandboxOptionDef option, int valueIndex)
    {
        var values = ValuesOf(option);
        if (valueIndex < 0 || valueIndex >= values.Count) return "?";
        return Zh("values", option.ValueSet, valueIndex.ToString()) ?? values[valueIndex].Label;
    }

    public string PresetLabel(SandboxPreset preset) => Zh("presets", preset.Name, "label") ?? preset.Label;

    public string PresetDescription(SandboxPreset preset) =>
        Zh("presets", preset.Name, "description") ?? preset.Description;

    public string PresetGroupLabel(SandboxPreset preset) =>
        Zh("presetGroups", preset.Group) ?? preset.GroupLabel;

    private string? Zh(params string[] path)
    {
        if (!Loc.Instance.IsChinese || Chinese is not { } node) return null;
        foreach (var part in path)
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(part, out node))
                return null;
        }
        return node.ValueKind == JsonValueKind.String ? node.GetString() : null;
    }
}

public sealed class SandboxValueSet
{
    public string Kind { get; init; } = "";
    public List<SandboxValue> Values { get; init; } = [];
}

public sealed class SandboxValue
{
    /// <summary>The number or flag the game applies (kept raw; the label is what people read).</summary>
    public JsonElement Value { get; init; }
    public string Label { get; init; } = "";
}

public sealed class SandboxOptionDef
{
    /// <summary>Position in the game's SandboxOptions enum — the two-letter code index.</summary>
    public int Index { get; init; }
    /// <summary>Registration order, which is the order the game writes options in.</summary>
    public int Order { get; init; }
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public string Kind { get; init; } = "";
    public string ValueSet { get; init; } = "";
    public int DefaultIndex { get; init; }
}

/// <summary>A built-in preset (the difficulties and the Official set): a name and a code.</summary>
public sealed class SandboxPreset
{
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public string Description { get; init; } = "";
    public string Group { get; init; } = "";
    public string GroupLabel { get; init; } = "";
    public int? Rating { get; init; }
    public bool IsDefault { get; init; }
    /// <summary>Empty for Nomad, which IS the defaults.</summary>
    public string Code { get; init; } = "";
}

/// <summary>The bundled tables, oldest first.</summary>
public static class SandboxTables
{
    private static readonly string[] Versions = ["3.0", "3.1", "3.2", "3.3"];

    private static readonly Lazy<IReadOnlyList<SandboxTable>> Loaded = new(Load);

    public static IReadOnlyList<SandboxTable> All => Loaded.Value;

    public static SandboxTable Latest => All[^1];

    public static SandboxTable? ByVersion(string version) =>
        All.FirstOrDefault(t => t.Version == version);

    /// <summary>
    /// The table for a server on <paramref name="version"/>: an exact match, else the newest
    /// table that isn't AHEAD of the server (a later table can describe options the server
    /// doesn't have). Null for pre-3.0 or an unknown version — guessing which table applies
    /// is exactly the mistake that writes a code meaning something else, so callers decide
    /// explicitly whether to fall back to <see cref="Latest"/>.
    /// </summary>
    public static SandboxTable? For(GameVersion? version)
    {
        if (version is null || version.Major < 3) return null;
        return All.LastOrDefault(t =>
        {
            var tv = GameVersion.Parse(t.Version)!;
            return tv.Major < version.Major || (tv.Major == version.Major && tv.Minor <= version.Minor);
        });
    }

    /// <summary>True when the server is newer than the table — the table may be stale.</summary>
    public static bool IsBehind(GameVersion? version, SandboxTable table)
    {
        var tv = GameVersion.Parse(table.Version);
        if (version is null || tv is null) return false;
        return version.Major > tv.Major || (version.Major == tv.Major && version.Minor > tv.Minor);
    }

    private static IReadOnlyList<SandboxTable> Load()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var asm = typeof(SandboxTables).Assembly;

        JsonElement? chinese = null;
        using (var zh = asm.GetManifestResourceStream("Kitsune7Den.Sandbox.zh-Hans.json"))
        {
            if (zh is not null)
                chinese = JsonDocument.Parse(zh).RootElement.Clone();
        }

        var tables = new List<SandboxTable>();
        foreach (var v in Versions)
        {
            using var stream = asm.GetManifestResourceStream($"Kitsune7Den.Sandbox.{v}.json")
                ?? throw new InvalidOperationException($"Missing embedded sandbox table {v}");
            var table = JsonSerializer.Deserialize<SandboxTable>(stream, options)
                ?? throw new InvalidDataException($"Unreadable sandbox table {v}");

            var key = "v" + v.Replace('.', '_');
            if (chinese is { } zhRoot && zhRoot.TryGetProperty(key, out var zhTable))
                table.Chinese = zhTable;
            tables.Add(table);
        }
        return tables;
    }
}
