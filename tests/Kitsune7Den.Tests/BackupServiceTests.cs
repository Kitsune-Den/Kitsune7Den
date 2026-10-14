using System.IO;
using System.IO.Compression;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.Tests;

/// <summary>
/// Tests the backup service's discovery, file operations, and backup listing.
///
/// Note: CreateBackupAsync / RestoreBackupAsync depend on
/// FindSaveDirectory which looks at %AppData%\7DaysToDie\Saves. We only
/// exercise the parts of BackupService that don't require that global state
/// so tests stay hermetic: BackupDirectory, GetBackups, DeleteBackup, and
/// the BackupInfo size/age formatting helpers.
///
/// The age text is localized and asserted in English, so this class shares the
/// "Localization" collection: it can't run while LocalizationTests has Chinese active.
/// </summary>
[Collection("Localization")]
public class BackupServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppSettings _settings;
    private readonly ConfigService _configService;
    private readonly BackupService _service;

    public BackupServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Kitsune7DenTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _settings = new AppSettings { ServerDirectory = _tempRoot };
        _configService = new ConfigService(_settings);
        _service = new BackupService(_settings, _configService);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void BackupDirectory_CreatedUnderServerDirectory()
    {
        var dir = _service.BackupDirectory;
        Assert.StartsWith(_tempRoot, dir);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void GetBackups_ReturnsEmpty_WhenNoZipsPresent()
    {
        var backups = _service.GetBackups();
        Assert.Empty(backups);
    }

    [Fact]
    public void GetBackups_ReturnsOneEntryPerZip_SortedNewestFirst()
    {
        // Drop three fake backup zips with distinct creation times
        var backupDir = _service.BackupDirectory;
        var first = Path.Combine(backupDir, "KitsuneDen_2026-04-01_10-00-00_manual.zip");
        var second = Path.Combine(backupDir, "KitsuneDen_2026-04-02_10-00-00_manual.zip");
        var third = Path.Combine(backupDir, "KitsuneDen_2026-04-03_10-00-00_scheduled.zip");

        // Write empty zips (BackupService uses the raw file, it doesn't validate contents for listing)
        foreach (var path in new[] { first, second, third })
            CreateEmptyZip(path);

        // Bump creation times so sort order is deterministic
        File.SetCreationTime(first,  new DateTime(2026, 4, 1, 10, 0, 0));
        File.SetCreationTime(second, new DateTime(2026, 4, 2, 10, 0, 0));
        File.SetCreationTime(third,  new DateTime(2026, 4, 3, 10, 0, 0));

        var backups = _service.GetBackups();
        Assert.Equal(3, backups.Count);
        Assert.Equal("KitsuneDen_2026-04-03_10-00-00_scheduled.zip", backups[0].FileName);
        Assert.Equal("scheduled", backups[0].BackupType);
        Assert.Equal("KitsuneDen_2026-04-02_10-00-00_manual.zip", backups[1].FileName);
        Assert.Equal("KitsuneDen_2026-04-01_10-00-00_manual.zip", backups[2].FileName);
    }

    [Fact]
    public void DeleteBackup_RemovesTheZip()
    {
        var backupDir = _service.BackupDirectory;
        var path = Path.Combine(backupDir, "KitsuneDen_2026-04-01_10-00-00_manual.zip");
        CreateEmptyZip(path);
        Assert.True(File.Exists(path));

        var backup = new BackupInfo { FilePath = path, FileName = Path.GetFileName(path) };
        var ok = _service.DeleteBackup(backup);

        Assert.True(ok);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(500L, "500 B")]
    [InlineData(2048L, "2.0 KB")]
    [InlineData(5L * 1024 * 1024, "5.0 MB")]
    [InlineData(2L * 1024 * 1024 * 1024, "2.00 GB")]
    public void BackupInfo_SizeText_FormatsBytes(long bytes, string expected)
    {
        var info = new BackupInfo { SizeBytes = bytes };
        Assert.Equal(expected, info.SizeText);
    }

    [Fact]
    public void BackupInfo_TimeAgo_JustNowUnderOneMinute()
    {
        var info = new BackupInfo { CreatedAt = DateTime.Now.AddSeconds(-30) };
        Assert.Equal("just now", info.TimeAgo);
    }

    [Fact]
    public void BackupInfo_TimeAgo_MinutesFormat()
    {
        var info = new BackupInfo { CreatedAt = DateTime.Now.AddMinutes(-15) };
        Assert.Contains("m ago", info.TimeAgo);
    }

    [Fact]
    public void BackupInfo_TimeAgo_HoursFormat()
    {
        var info = new BackupInfo { CreatedAt = DateTime.Now.AddHours(-3) };
        Assert.Contains("h ago", info.TimeAgo);
    }

    [Fact]
    public void BackupInfo_TimeAgo_DaysFormat()
    {
        var info = new BackupInfo { CreatedAt = DateTime.Now.AddDays(-2) };
        Assert.Contains("d ago", info.TimeAgo);
    }

    private static void CreateEmptyZip(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        zip.CreateEntry("placeholder.txt");
    }
}
