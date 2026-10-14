using System.IO;
using Kitsune7Den.Services;

namespace Kitsune7Den.Tests;

public class GameVersionTests : IDisposable
{
    private readonly string _serverDir;

    public GameVersionTests()
    {
        _serverDir = Path.Combine(Path.GetTempPath(), "Kitsune7DenVersion_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_serverDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_serverDir, recursive: true); }
        catch { /* best effort */ }
    }

    [Theory]
    [InlineData("V 3.3.0 (b18)", 3, 3, 18)]
    [InlineData("v3.2", 3, 2, null)]
    [InlineData("3.1", 3, 1, null)]
    [InlineData("v3", 3, 0, null)]
    [InlineData("V 2.6.0 (b14)", 2, 6, 14)]
    public void Parse_UnderstandsEveryWay7D2DWritesAVersion(string text, int major, int minor, int? build)
    {
        Assert.Equal(new GameVersion(major, minor, build), GameVersion.Parse(text));
    }

    [Theory]
    [InlineData("public")]
    [InlineData("latest_experimental")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_NamedBranches_AreNotVersions(string? text)
    {
        Assert.Null(GameVersion.Parse(text));
    }

    [Theory]
    [InlineData("2026-10-03T09:59:10 0.001 INF Version: V 3.3.0 (b18) Compatibility Version: V 3.3.0, Build: Windows 64 Bit", 3, 3, 18)]
    [InlineData("2026-10-03T09:40:35 0.002 INF Version: V 3.2.0 (b10) Compatibility Version: V 3.2.0, Build: LinuxServer 64 Bit", 3, 2, 10)]
    [InlineData("Game version: V 3.0.1 (b4) Compatibility Version: V 3.0.1", 3, 0, 4)]
    public void FromLogLine_ReadsTheGamesVersionLine(string line, int major, int minor, int build)
    {
        Assert.Equal(new GameVersion(major, minor, build), GameVersionService.FromLogLine(line));
    }

    [Fact]
    public void FromLogLine_IgnoresOtherLines()
    {
        Assert.Null(GameVersionService.FromLogLine("2026-10-03T09:59:10 0.001 INF Loading mod Harmony version 2.2"));
    }

    [Fact]
    public void Detect_PrefersTheNewestServerLog()
    {
        var old = Path.Combine(_serverDir, "output_log_dedi__2026-09-01__10-00-00.txt");
        File.WriteAllText(old, "x INF Version: V 3.2.0 (b10) Compatibility Version: V 3.2.0\n");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        File.WriteAllText(Path.Combine(_serverDir, "output_log_dedi__2026-10-10__10-00-00.txt"),
            "x INF Starting\nx INF Version: V 3.3.0 (b18) Compatibility Version: V 3.3.0\n");

        var detected = GameVersionService.Detect(_serverDir, "public");

        Assert.Equal(GameVersionSource.ServerLog, detected.Source);
        Assert.Equal(new GameVersion(3, 3, 18), detected.Version);
        Assert.True(detected.IsSandboxEra);
    }

    [Fact]
    public void Detect_FallsBackToAPinnedSteamBranch()
    {
        var detected = GameVersionService.Detect(_serverDir, "v3.2");
        Assert.Equal(GameVersionSource.SteamBranch, detected.Source);
        Assert.Equal(new GameVersion(3, 2), detected.Version);
    }

    [Fact]
    public void Detect_SandboxFilesPinTheEraButNotTheMinor()
    {
        var config = Path.Combine(_serverDir, "Data", "Config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "sandbox_overrides.xml"), "<sandbox_overrides />");

        var detected = GameVersionService.Detect(_serverDir, "public");

        Assert.Equal(GameVersionSource.InstallFiles, detected.Source);
        Assert.Null(detected.Version);
        Assert.True(detected.IsSandboxEra);
    }

    [Fact]
    public void Detect_NothingToGoOn_IsUnknown()
    {
        var detected = GameVersionService.Detect(_serverDir, "public");
        Assert.Equal(GameVersionSource.None, detected.Source);
        Assert.False(detected.IsSandboxEra);
    }

    [Fact]
    public void Detect_A2xServer_IsNotSandboxEra()
    {
        File.WriteAllText(Path.Combine(_serverDir, "output_log_dedi__2026-01-01__10-00-00.txt"),
            "x INF Version: V 2.6.0 (b14) Compatibility Version: V 2.6.0\n");
        Assert.False(GameVersionService.Detect(_serverDir, "public").IsSandboxEra);
    }
}
