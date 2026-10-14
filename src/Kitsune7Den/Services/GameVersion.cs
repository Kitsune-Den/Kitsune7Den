using System.IO;
using System.Text.RegularExpressions;
using Kitsune7Den.Models;

namespace Kitsune7Den.Services;

/// <summary>A 7D2D game version, e.g. "V 3.3.0 (b18)" → 3.3, build 18.</summary>
public sealed partial record GameVersion(int Major, int Minor, int? Build = null)
{
    /// <summary>
    /// Parse anything 7D2D calls a version: the log line ("V 3.3.0 (b18)"), a SteamCMD
    /// branch ("v3.2", "v3"), or a bare "3.1". Null for "public", "latest_experimental", "".
    /// </summary>
    public static GameVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var m = MajorMinorRegex().Match(text);
        if (m.Success)
        {
            var build = BuildRegex().Match(text);
            return new GameVersion(
                int.Parse(m.Groups[1].Value),
                int.Parse(m.Groups[2].Value),
                build.Success ? int.Parse(build.Groups[1].Value) : null);
        }

        var only = MajorOnlyRegex().Match(text.Trim());
        return only.Success ? new GameVersion(int.Parse(only.Groups[1].Value), 0) : null;
    }

    /// <summary>3.0 ("Dead Hot Summer") moved the gameplay rules into the Sandbox / SandboxCode.</summary>
    public bool HasSandbox => Major >= 3;

    public override string ToString() => Build is { } b ? $"{Major}.{Minor} (b{b})" : $"{Major}.{Minor}";

    [GeneratedRegex(@"(\d+)\.(\d+)")]
    private static partial Regex MajorMinorRegex();

    [GeneratedRegex(@"\(b(\d+)\)")]
    private static partial Regex BuildRegex();

    [GeneratedRegex(@"^[vV]?(\d+)$")]
    private static partial Regex MajorOnlyRegex();
}

/// <summary>Where a detected version came from — the UI says how sure it is.</summary>
public enum GameVersionSource
{
    None,
    /// <summary>The game's own "Version: V 3.3.0 (b18)" log line. Exact.</summary>
    ServerLog,
    /// <summary>A version-pinned SteamCMD branch such as "v3.2".</summary>
    SteamBranch,
    /// <summary>Only that the install is 3.x (Sandbox files present), minor unknown.</summary>
    InstallFiles,
}

public sealed record DetectedGameVersion(GameVersion? Version, GameVersionSource Source)
{
    public static readonly DetectedGameVersion Unknown = new(null, GameVersionSource.None);

    /// <summary>True for 3.0+, including installs whose minor version is unknown.</summary>
    public bool IsSandboxEra => Version?.HasSandbox == true || Source == GameVersionSource.InstallFiles;
}

/// <summary>
/// Works out which 7D2D version a server is running without starting it. Kitsune7Den is
/// an outside app, so it can't ask the game; it reads the evidence the install leaves.
/// </summary>
public partial class GameVersionService
{
    private readonly AppSettings _settings;

    public GameVersionService(AppSettings settings)
    {
        _settings = settings;
    }

    public DetectedGameVersion Detect() => Detect(_settings.ServerDirectory, _settings.ServerBranch);

    public static DetectedGameVersion Detect(string? serverDirectory, string? steamBranch)
    {
        if (!string.IsNullOrEmpty(serverDirectory) && Directory.Exists(serverDirectory))
        {
            var fromLog = FromLogs(serverDirectory);
            if (fromLog is not null) return new DetectedGameVersion(fromLog, GameVersionSource.ServerLog);
        }

        var fromBranch = GameVersion.Parse(steamBranch);
        if (fromBranch is not null) return new DetectedGameVersion(fromBranch, GameVersionSource.SteamBranch);

        // 3.0 added Data/Config/sandbox_overrides.xml; its presence pins the era, not the minor.
        if (!string.IsNullOrEmpty(serverDirectory) &&
            File.Exists(Path.Combine(serverDirectory, "Data", "Config", "sandbox_overrides.xml")))
            return new DetectedGameVersion(null, GameVersionSource.InstallFiles);

        return DetectedGameVersion.Unknown;
    }

    /// <summary>
    /// The newest log that names a version. A log only records the build that wrote it,
    /// so the newest one wins — an update since then will show on the next start.
    /// </summary>
    private static GameVersion? FromLogs(string serverDirectory)
    {
        var logs = new List<string>();
        foreach (var dir in new[] { serverDirectory, Path.Combine(serverDirectory, "7DaysToDieServer_Data") })
        {
            if (Directory.Exists(dir))
                logs.AddRange(Directory.GetFiles(dir, "output_log*.txt"));
        }

        foreach (var log in logs.OrderByDescending(File.GetLastWriteTimeUtc).Take(5))
        {
            var version = FromLogFile(log);
            if (version is not null) return version;
        }
        return null;
    }

    /// <summary>The version line is printed within the first few hundred lines of startup.</summary>
    public static GameVersion? FromLogFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            for (var i = 0; i < 400 && reader.ReadLine() is { } line; i++)
            {
                var version = FromLogLine(line);
                if (version is not null) return version;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>"... INF Version: V 3.3.0 (b18) Compatibility Version: V 3.3, Build: Windows 64 Bit"</summary>
    public static GameVersion? FromLogLine(string line)
    {
        var m = LogVersionRegex().Match(line);
        return m.Success ? GameVersion.Parse(m.Groups[1].Value) : null;
    }

    [GeneratedRegex(@"\bVersion:\s*(V\s*\d+\.\d+(?:\.\d+)?\s*(?:\(b\d+\))?)", RegexOptions.IgnoreCase)]
    private static partial Regex LogVersionRegex();
}
