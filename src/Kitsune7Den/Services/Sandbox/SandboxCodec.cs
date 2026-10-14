namespace Kitsune7Den.Services.Sandbox;

/// <summary>
/// 7 Days to Die 3.0+ SandboxCode encoder/decoder — the same format the game's own
/// "copy code" button writes. Ported from PackRelay's seven-days-sandbox.ts.
///
/// <code>
///   code   := &lt;version letter&gt; &lt;triple&gt;*
///   triple := &lt;option index, base 26, two letters&gt; &lt;value index, one letter&gt;
/// </code>
///
/// "AA" is option 0, "GI" is 164; the third letter indexes into that option's OWN value
/// list. Options at their default are left out (loading a code resets everything to
/// default first), and the game emits the rest in registration order. No checksum, no
/// escaping. The tables differ between game versions under the same version letter, so
/// every call takes the table for the server's version.
/// </summary>
public static class SandboxCodec
{
    public const int MaxValues = 26;
    public const int MaxOptions = 26 * 26;

    public static string IndexToAlpha(int index)
    {
        if (index is < 0 or >= MaxValues)
            throw new ArgumentOutOfRangeException(nameof(index), $"sandbox value index out of range: {index}");
        return ((char)('A' + index)).ToString();
    }

    public static string IndexToAlpha2(int index)
    {
        if (index is < 0 or >= MaxOptions)
            throw new ArgumentOutOfRangeException(nameof(index), $"sandbox option index out of range: {index}");
        return $"{(char)('A' + index / 26)}{(char)('A' + index % 26)}";
    }

    /// <summary>'A'–'Z' (any case) → 0–25, or -1.</summary>
    public static int AlphaToIndex(char c)
    {
        var i = char.ToUpperInvariant(c) - 'A';
        return i is >= 0 and < 26 ? i : -1;
    }

    /// <summary>"AA"–"ZZ" → 0–675, or -1.</summary>
    public static int Alpha2ToIndex(ReadOnlySpan<char> s)
    {
        if (s.Length != 2) return -1;
        var hi = AlphaToIndex(s[0]);
        var lo = AlphaToIndex(s[1]);
        return hi < 0 || lo < 0 ? -1 : hi * 26 + lo;
    }

    /// <summary>Every option at its default — the editor's starting point.</summary>
    public static Dictionary<string, int> Defaults(SandboxTable table) =>
        table.Options.ToDictionary(o => o.Name, o => o.DefaultIndex);

    /// <summary>
    /// Read a code against a table. Never throws: a malformed code comes back with
    /// <see cref="DecodedSandbox.Ok"/> false, and triples this version can't place are
    /// kept in <see cref="DecodedSandbox.Unknown"/> — the game skips them silently, we
    /// keep them so a save doesn't strip settings a newer server understands.
    /// </summary>
    public static DecodedSandbox Decode(string? code, SandboxTable table)
    {
        var raw = (code ?? "").Trim();
        var result = new DecodedSandbox { CodeVersion = raw.Length > 0 ? raw[..1] : "" };

        if (raw.Length == 0) return result with { Error = SandboxDecodeError.Empty };
        if (!raw.All(char.IsAsciiLetter)) return result with { Error = SandboxDecodeError.NotLetters };
        if (!string.Equals(raw[..1], table.CodeVersion, StringComparison.OrdinalIgnoreCase))
            return result with { Error = SandboxDecodeError.WrongVersion };

        var body = raw[1..];
        var count = body.Length / 3;
        var seen = new HashSet<int>();

        for (var i = 0; i < count; i++)
        {
            var triple = body.Substring(i * 3, 3);
            var optionIndex = Alpha2ToIndex(triple.AsSpan(0, 2));
            var valueIndex = AlphaToIndex(triple[2]);
            var option = table.OptionByIndex(optionIndex);
            if (option is null)
            {
                result.Unknown.Add(new SandboxUnknownEntry(optionIndex, valueIndex, triple, "unknown-option"));
                continue;
            }
            if (valueIndex >= table.ValuesOf(option).Count)
            {
                result.Unknown.Add(new SandboxUnknownEntry(optionIndex, valueIndex, triple, "value-out-of-range"));
                continue;
            }
            // A repeated option would make the game's Dictionary.Add throw on load;
            // keep the first, which is what a hand-edited code most likely meant.
            if (!seen.Add(optionIndex)) continue;
            result.Entries.Add(new SandboxEntry(option, valueIndex));
        }

        return result with { Ok = true, Trailing = body[(count * 3)..] };
    }

    /// <summary>Every option in the table, at its default unless the code says otherwise.</summary>
    public static Dictionary<string, int> SelectionFromCode(string? code, SandboxTable table)
    {
        var selection = Defaults(table);
        foreach (var e in Decode(code, table).Entries)
            selection[e.Option.Name] = e.ValueIndex;
        return selection;
    }

    /// <summary>
    /// Write a code. Options at their default, and names the table doesn't know, are left
    /// out; the rest go in the game's own order, so the result is byte-identical to the
    /// game's copy button for the same settings. <paramref name="passthrough"/> re-appends
    /// triples this table couldn't read.
    /// </summary>
    public static string Encode(
        IReadOnlyDictionary<string, int> selection,
        SandboxTable table,
        IEnumerable<SandboxUnknownEntry>? passthrough = null)
    {
        var chosen = new List<(SandboxOptionDef Option, int ValueIndex)>();
        foreach (var (name, valueIndex) in selection)
        {
            var option = table.OptionByName(name);
            if (option is null || valueIndex == option.DefaultIndex) continue;
            var count = table.ValuesOf(option).Count;
            if (valueIndex < 0 || valueIndex >= count)
                throw new ArgumentOutOfRangeException(nameof(selection),
                    $"sandbox option {name}: value index {valueIndex} is outside its {count} values");
            chosen.Add((option, valueIndex));
        }

        var sb = new System.Text.StringBuilder(table.CodeVersion);
        foreach (var (option, valueIndex) in chosen.OrderBy(c => c.Option.Order))
            sb.Append(IndexToAlpha2(option.Index)).Append(IndexToAlpha(valueIndex));
        foreach (var u in passthrough ?? [])
            sb.Append(u.Raw.ToUpperInvariant());
        return sb.ToString();
    }

    /// <summary>
    /// A preset is applied like a code: everything to default, then its values on top.
    /// (Nomad's code is empty: it IS the defaults.)
    /// </summary>
    public static Dictionary<string, int> SelectionFromPreset(SandboxPreset preset, SandboxTable table) =>
        string.IsNullOrEmpty(preset.Code) ? Defaults(table) : SelectionFromCode(preset.Code, table);

    /// <summary>The preset whose settings exactly match <paramref name="selection"/>, if any.</summary>
    public static SandboxPreset? MatchingPreset(IReadOnlyDictionary<string, int> selection, SandboxTable table)
    {
        var code = Encode(selection, table);
        return table.Presets.FirstOrDefault(p =>
            string.Equals(Encode(SelectionFromPreset(p, table), table), code, StringComparison.Ordinal));
    }
}

public enum SandboxDecodeError
{
    None,
    Empty,
    NotLetters,
    WrongVersion,
}

public sealed record DecodedSandbox
{
    public bool Ok { get; init; }
    public SandboxDecodeError Error { get; init; }
    public string CodeVersion { get; init; } = "";
    public List<SandboxEntry> Entries { get; init; } = [];
    public List<SandboxUnknownEntry> Unknown { get; init; } = [];
    /// <summary>Leftover characters when the length isn't 1 + 3n. The game ignores them.</summary>
    public string Trailing { get; init; } = "";
}

public sealed record SandboxEntry(SandboxOptionDef Option, int ValueIndex);

public sealed record SandboxUnknownEntry(int OptionIndex, int ValueIndex, string Raw, string Reason);
