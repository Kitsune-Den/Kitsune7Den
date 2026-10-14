using Kitsune7Den.Localization;

namespace Kitsune7Den.Models;

public class ModInfo
{
    public string FolderName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public long FolderSizeBytes { get; set; }
    public bool IsEnabled { get; set; }

    public string SizeText => FolderSizeBytes switch
    {
        < 1024 => $"{FolderSizeBytes} B",
        < 1024 * 1024 => $"{FolderSizeBytes / 1024.0:F1} KB",
        _ => $"{FolderSizeBytes / (1024.0 * 1024):F1} MB"
    };

    public string StatusText => Loc.T(IsEnabled ? "Mods_StatusEnabled" : "Mods_StatusDisabled");
}
