using System.IO;
using System.IO.Compression;
using Kitsune7Den.Localization;
using Kitsune7Den.Models;

namespace Kitsune7Den.Services;

public class BackupService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly ConfigService _configService;
    private System.Threading.Timer? _scheduledTimer;

    public event Action<string>? StatusChanged;

    public bool IsScheduleRunning => _scheduledTimer is not null;
    public DateTime? NextScheduledBackup { get; private set; }
    public DateTime? LastScheduledBackup { get; private set; }

    public BackupService(AppSettings settings, ConfigService configService)
    {
        _settings = settings;
        _configService = configService;
    }

    /// <summary>
    /// Start the scheduled backup timer based on settings.
    /// </summary>
    public void StartSchedule()
    {
        StopSchedule();

        if (!_settings.ScheduledBackupsEnabled || _settings.BackupIntervalMinutes <= 0)
            return;

        var intervalMs = _settings.BackupIntervalMinutes * 60 * 1000;
        NextScheduledBackup = DateTime.Now.AddMinutes(_settings.BackupIntervalMinutes);

        _scheduledTimer = new System.Threading.Timer(async _ =>
        {
            StatusChanged?.Invoke(Loc.T("Backups_RunningScheduled"));
            await CreateBackupAsync("scheduled");
            LastScheduledBackup = DateTime.Now;
            NextScheduledBackup = DateTime.Now.AddMinutes(_settings.BackupIntervalMinutes);
        }, null, intervalMs, intervalMs);

        StatusChanged?.Invoke(Loc.F("Backups_ScheduleEnabled", FormatInterval(_settings.BackupIntervalMinutes)));
    }

    /// <summary>
    /// Stop the scheduled backup timer.
    /// </summary>
    public void StopSchedule()
    {
        _scheduledTimer?.Dispose();
        _scheduledTimer = null;
        NextScheduledBackup = null;
    }

    private static string FormatInterval(int minutes) => minutes switch
    {
        < 60 => Loc.F("Backups_IntervalMinutes", minutes),
        60 => Loc.T("Backups_IntervalOneHour"),
        < 1440 => Loc.F("Backups_IntervalHours", minutes / 60),
        1440 => Loc.T("Backups_IntervalOneDay"),
        _ => Loc.F("Backups_IntervalDays", minutes / 1440)
    };

    /// <summary>
    /// The directory where backups are stored.
    /// </summary>
    public string BackupDirectory
    {
        get
        {
            var dir = Path.Combine(
                string.IsNullOrEmpty(_settings.ServerDirectory)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                    : _settings.ServerDirectory,
                "Kitsune7Den-Backups");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Finds the active save game directory based on serverconfig.xml settings.
    /// </summary>
    public string? FindSaveDirectory()
    {
        var savesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "7DaysToDie", "Saves");

        if (!Directory.Exists(savesRoot)) return null;

        // Try to read world and game name from config
        var props = _configService.LoadConfig();
        var worldName = props.FirstOrDefault(p => p.Name == "GameWorld")?.Value;
        var gameName = props.FirstOrDefault(p => p.Name == "GameName")?.Value;

        if (!string.IsNullOrEmpty(worldName) && !string.IsNullOrEmpty(gameName))
        {
            // For RWG worlds, the world folder is inside RWG/
            var candidates = new[]
            {
                Path.Combine(savesRoot, worldName, gameName),
                Path.Combine(savesRoot, "RWG", gameName),
            };

            foreach (var path in candidates)
            {
                if (Directory.Exists(path)) return path;
            }
        }

        // Fallback: search for directories with save data
        var allSaves = Directory.GetDirectories(savesRoot, "*", SearchOption.AllDirectories)
            .Where(d => File.Exists(Path.Combine(d, "main.ttw")))
            .OrderByDescending(d => new DirectoryInfo(d).LastWriteTime)
            .ToArray();

        return allSaves.FirstOrDefault();
    }

    /// <summary>
    /// Create a backup of the current save game.
    /// </summary>
    public async Task<BackupInfo?> CreateBackupAsync(string type = "manual", string notes = "")
    {
        var saveDir = FindSaveDirectory();
        if (saveDir is null)
        {
            StatusChanged?.Invoke(Loc.T("Backups_ErrNoSaveDir"));
            return null;
        }

        var saveDirInfo = new DirectoryInfo(saveDir);
        var worldName = saveDirInfo.Parent?.Name ?? "Unknown";
        var gameName = saveDirInfo.Name;
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var fileName = $"{gameName}_{timestamp}_{type}.zip";
        var backupPath = Path.Combine(BackupDirectory, fileName);

        StatusChanged?.Invoke(Loc.F("Backups_Creating", gameName));

        try
        {
            await Task.Run(() =>
            {
                ZipFile.CreateFromDirectory(saveDir, backupPath, CompressionLevel.Optimal, includeBaseDirectory: true);
            });

            var fi = new FileInfo(backupPath);
            var backup = new BackupInfo
            {
                FileName = fileName,
                FilePath = backupPath,
                WorldName = worldName,
                GameName = gameName,
                CreatedAt = DateTime.Now,
                SizeBytes = fi.Length,
                BackupType = type,
                Notes = notes
            };

            StatusChanged?.Invoke(Loc.F("Backups_Created", fileName, backup.SizeText));

            // Auto-prune old backups
            PruneOldBackups();

            return backup;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(Loc.F("Backups_CreateFailed", ex.Message));
            return null;
        }
    }

    /// <summary>
    /// Restore a backup, creating a safety backup first.
    /// </summary>
    public async Task<bool> RestoreBackupAsync(BackupInfo backup)
    {
        var saveDir = FindSaveDirectory();
        if (saveDir is null)
        {
            StatusChanged?.Invoke(Loc.T("Backups_ErrNoRestoreDir"));
            return false;
        }

        // Safety: create a pre-restore backup
        StatusChanged?.Invoke(Loc.T("Backups_CreatingSafety"));
        await CreateBackupAsync("pre-restore", $"Auto-backup before restoring {backup.FileName}");

        StatusChanged?.Invoke(Loc.F("Backups_Restoring", backup.FileName));

        try
        {
            await Task.Run(() =>
            {
                // Clear the save directory
                var di = new DirectoryInfo(saveDir);
                foreach (var file in di.GetFiles()) file.Delete();
                foreach (var dir in di.GetDirectories()) dir.Delete(recursive: true);

                // Extract backup
                ZipFile.ExtractToDirectory(backup.FilePath, saveDir, overwriteFiles: true);

                // If the zip contained the folder inside, move contents up
                var innerDirs = Directory.GetDirectories(saveDir);
                if (innerDirs.Length == 1 && File.Exists(Path.Combine(innerDirs[0], "main.ttw")))
                {
                    var inner = innerDirs[0];
                    foreach (var file in Directory.GetFiles(inner))
                        File.Move(file, Path.Combine(saveDir, Path.GetFileName(file)), overwrite: true);
                    foreach (var dir in Directory.GetDirectories(inner))
                        Directory.Move(dir, Path.Combine(saveDir, Path.GetFileName(dir)));
                    Directory.Delete(inner);
                }
            });

            StatusChanged?.Invoke(Loc.F("Backups_Restored", backup.FileName));
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(Loc.F("Backups_RestoreFailed", ex.Message));
            return false;
        }
    }

    /// <summary>
    /// List all available backups.
    /// </summary>
    public List<BackupInfo> GetBackups()
    {
        if (!Directory.Exists(BackupDirectory))
            return [];

        return Directory.GetFiles(BackupDirectory, "*.zip")
            .Select(f =>
            {
                var fi = new FileInfo(f);
                var parts = Path.GetFileNameWithoutExtension(f).Split('_');
                return new BackupInfo
                {
                    FileName = fi.Name,
                    FilePath = f,
                    GameName = parts.Length > 0 ? parts[0] : fi.Name,
                    CreatedAt = fi.CreationTime,
                    SizeBytes = fi.Length,
                    BackupType = parts.Length >= 4 ? parts[3] : "manual"
                };
            })
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Delete a specific backup.
    /// </summary>
    public bool DeleteBackup(BackupInfo backup)
    {
        if (!File.Exists(backup.FilePath)) return false;
        File.Delete(backup.FilePath);
        StatusChanged?.Invoke(Loc.F("Backups_Deleted", backup.FileName));
        return true;
    }

    /// <summary>
    /// Keep only the newest N backups.
    /// </summary>
    private void PruneOldBackups()
    {
        var backups = GetBackups();
        var max = _settings.MaxBackups > 0 ? _settings.MaxBackups : 20;
        if (backups.Count <= max) return;

        var toDelete = backups.Skip(max);
        foreach (var b in toDelete)
        {
            try
            {
                File.Delete(b.FilePath);
                StatusChanged?.Invoke(Loc.F("Backups_Pruned", b.FileName));
            }
            catch { /* best effort */ }
        }
    }

    public void Dispose()
    {
        StopSchedule();
    }
}
