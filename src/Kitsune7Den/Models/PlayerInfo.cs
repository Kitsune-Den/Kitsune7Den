using Kitsune7Den.Localization;

namespace Kitsune7Den.Models;

public class PlayerInfo
{
    public int EntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string CrossId { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int Health { get; set; }
    public int Deaths { get; set; }
    public int ZombieKills { get; set; }
    public int PlayerKills { get; set; }
    public int Score { get; set; }
    public int Level { get; set; }
    public int Ping { get; set; }
    public string Ip { get; set; } = string.Empty;
    public TimeSpan Playtime { get; set; }

    // Admin info (populated from AdminService)
    public bool IsAdmin { get; set; }
    public int PermissionLevel { get; set; } = 1000;
    public string PermissionLabel =>
        PermissionLevel < 1000 ? PermissionLabels.For(PermissionLevel) : "";

    public string PositionText => $"{X:F0}, {Y:F0}, {Z:F0}";
}

/// <summary>
/// Display names for serveradmin.xml permission levels. Display only: logic
/// compares the numeric level, never these strings. Falls back to English when a
/// key is missing so the label is never a raw key.
/// </summary>
public static class PermissionLabels
{
    public static string For(int level) => level switch
    {
        0 => Loc.TryGet("Perm_Owner") ?? "Owner",
        1 => Loc.TryGet("Perm_Admin") ?? "Admin",
        2 => Loc.TryGet("Perm_Moderator") ?? "Moderator",
        _ => string.Format(Loc.Instance.Culture, Loc.TryGet("Perm_Level") ?? "Level {0}", level)
    };
}
