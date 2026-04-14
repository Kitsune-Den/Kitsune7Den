using System.IO;
using System.Text.Json;

namespace Kitsune7Den.Models;

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kitsune7Den", "settings.json");

    public string ServerExePath { get; set; } = string.Empty;
    public string ServerDirectory { get; set; } = string.Empty;
    public int TelnetPort { get; set; } = 8081;
    public string TelnetPassword { get; set; } = string.Empty;

    // SteamCMD
    public string SteamCmdPath { get; set; } = string.Empty;
    public string ServerInstallDirectory { get; set; } = string.Empty;
    public string ServerBranch { get; set; } = "public"; // public, latest_experimental, etc.
    public bool AutoUpdateOnStart { get; set; }
    public bool ValidateOnUpdate { get; set; } = true;

    // App
    public string Theme { get; set; } = "Kitsune";

    // Window
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public double WindowLeft { get; set; } = -1;
    public double WindowTop { get; set; } = -1;
    public bool WindowMaximized { get; set; }

    // Backups
    public bool ScheduledBackupsEnabled { get; set; }
    public int BackupIntervalMinutes { get; set; } = 360; // default 6 hours
    public int MaxBackups { get; set; } = 20;

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        var json = File.ReadAllText(SettingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }
}
