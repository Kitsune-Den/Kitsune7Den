using System.IO;
using System.Text.Json;

namespace Kitsune7Den.Models;

public class AppSettings
{
    /// <summary>Set to redirect settings to another file. The test project uses it so tests never touch real settings.</summary>
    public const string PathOverrideVariable = "KITSUNE7DEN_SETTINGS_PATH";

    private static string SettingsPath =>
        Environment.GetEnvironmentVariable(PathOverrideVariable) is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(
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

    // Extra arguments for 7DaysToDieServer.exe, like startdedicated.bat's %*
    public string ServerLaunchArguments { get; set; } = string.Empty;

    // App
    public string Theme { get; set; } = "Kitsune";
    public string Language { get; set; } = string.Empty; // blank = follow Windows

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

    // Automatic restarts
    public bool CrashRestartEnabled { get; set; }
    public int CrashRestartMaxAttempts { get; set; } = 3;
    public int CrashRestartWindowMinutes { get; set; } = 15;
    public bool ScheduledRestartEnabled { get; set; }
    public string ScheduledRestartMode { get; set; } = "Daily"; // Daily | Interval
    public string ScheduledRestartTimes { get; set; } = "04:00";
    public int ScheduledRestartIntervalHours { get; set; } = 6;
    public string RestartWarningMinutes { get; set; } = "10, 5, 1";
    public string RestartWarningMessage { get; set; } = string.Empty; // blank = default in the UI language
    public string RestartFinalMessage { get; set; } = string.Empty;

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
