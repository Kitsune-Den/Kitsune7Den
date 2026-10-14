using Kitsune7Den.Localization;

namespace Kitsune7Den.Models;

public class BackupInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long SizeBytes { get; set; }
    public string BackupType { get; set; } = "manual"; // manual, scheduled, pre-restore
    public string Notes { get; set; } = string.Empty;

    public string SizeText => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{SizeBytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    public string TimeAgo
    {
        get
        {
            var ago = DateTime.Now - CreatedAt;
            if (ago.TotalMinutes < 1) return Loc.T("Backups_JustNow");
            if (ago.TotalMinutes < 60) return Loc.F("Backups_MinutesAgo", (int)ago.TotalMinutes);
            if (ago.TotalHours < 24) return Loc.F("Backups_HoursAgo", (int)ago.TotalHours);
            if (ago.TotalDays < 7) return Loc.F("Backups_DaysAgo", (int)ago.TotalDays);
            return CreatedAt.ToString("yyyy-MM-dd HH:mm", Loc.Instance.Culture);
        }
    }
}
