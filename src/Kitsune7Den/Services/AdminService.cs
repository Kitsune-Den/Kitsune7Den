using System.IO;
using System.Xml.Linq;
using Kitsune7Den.Models;

namespace Kitsune7Den.Services;

public class AdminService
{
    private readonly AppSettings _settings;

    // 7D2D stores serveradmin.xml in the saves folder, not the server install dir
    private static readonly string[] SearchPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7DaysToDie", "Saves", "serveradmin.xml"),
    ];

    public AdminService(AppSettings settings)
    {
        _settings = settings;
    }

    private string? FindAdminFile()
    {
        // Check configured server directory first
        if (!string.IsNullOrEmpty(_settings.ServerDirectory))
        {
            var inServerDir = Path.Combine(_settings.ServerDirectory, "serveradmin.xml");
            if (File.Exists(inServerDir)) return inServerDir;
        }

        // Check standard locations
        foreach (var path in SearchPaths)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    public string? AdminFilePath => FindAdminFile();

    /// <summary>
    /// Get all admin users from serveradmin.xml
    /// </summary>
    public List<AdminUser> GetAdmins()
    {
        var path = FindAdminFile();
        if (path is null) return [];

        var doc = XDocument.Load(path);
        var users = doc.Root?.Element("users")?.Elements("user") ?? [];

        return users.Select(u => new AdminUser
        {
            Platform = u.Attribute("platform")?.Value ?? "",
            UserId = u.Attribute("userid")?.Value ?? "",
            Name = u.Attribute("name")?.Value ?? "",
            PermissionLevel = int.TryParse(u.Attribute("permission_level")?.Value, out var pl) ? pl : 1000
        }).ToList();
    }

    /// <summary>
    /// Check if a player is an admin (by matching their platform ID)
    /// </summary>
    public bool IsAdmin(string platformId)
    {
        var admins = GetAdmins();
        // platformId from telnet is like "Steam_76561..." or "EOS_000285..."
        // serveradmin.xml stores just the ID portion
        var idPart = platformId.Contains('_') ? platformId.Split('_', 2)[1] : platformId;
        return admins.Any(a => a.UserId.Equals(idPart, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get the permission level for a player (1000 = default/no admin)
    /// </summary>
    public int GetPermissionLevel(string platformId)
    {
        var admins = GetAdmins();
        var idPart = platformId.Contains('_') ? platformId.Split('_', 2)[1] : platformId;
        var admin = admins.FirstOrDefault(a => a.UserId.Equals(idPart, StringComparison.OrdinalIgnoreCase));
        return admin?.PermissionLevel ?? 1000;
    }

    /// <summary>
    /// Add or update an admin in serveradmin.xml
    /// </summary>
    public bool SetAdmin(string platform, string userId, string name, int permissionLevel)
    {
        var path = FindAdminFile();
        if (path is null) return false;

        var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var usersElement = doc.Root?.Element("users");
        if (usersElement is null) return false;

        // Find existing entry
        var existing = usersElement.Elements("user")
            .FirstOrDefault(u => u.Attribute("userid")?.Value == userId);

        if (existing is not null)
        {
            existing.SetAttributeValue("permission_level", permissionLevel.ToString());
            existing.SetAttributeValue("name", name);
        }
        else
        {
            usersElement.Add(new XElement("user",
                new XAttribute("platform", platform),
                new XAttribute("userid", userId),
                new XAttribute("name", name),
                new XAttribute("permission_level", permissionLevel.ToString())));
        }

        doc.Save(path);
        return true;
    }

    /// <summary>
    /// Remove an admin from serveradmin.xml
    /// </summary>
    public bool RemoveAdmin(string userId)
    {
        var path = FindAdminFile();
        if (path is null) return false;

        var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var usersElement = doc.Root?.Element("users");
        if (usersElement is null) return false;

        var existing = usersElement.Elements("user")
            .FirstOrDefault(u => u.Attribute("userid")?.Value == userId);

        if (existing is null) return false;

        existing.Remove();
        doc.Save(path);
        return true;
    }
}

public class AdminUser
{
    public string Platform { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public int PermissionLevel { get; set; } = 1000;

    public string PermissionLabel => PermissionLabels.For(PermissionLevel);
}
