using System.IO;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.Tests;

/// <summary>
/// Tests the serveradmin.xml parser and the give/remove admin writers.
/// The real file lives in %AppData%\7DaysToDie\Saves — for tests we
/// fall back to the ServerDirectory copy that AdminService also checks.
/// Shares the "Localization" collection so permission labels are never read while
/// LocalizationTests has Chinese active.
/// </summary>
[Collection("Localization")]
public class AdminServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppSettings _settings;
    private readonly AdminService _service;

    public AdminServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Kitsune7DenTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _settings = new AppSettings { ServerDirectory = _tempRoot };
        _service = new AdminService(_settings);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best effort */ }
    }

    private void WriteAdminFile(string xml) =>
        File.WriteAllText(Path.Combine(_tempRoot, "serveradmin.xml"), xml);

    private const string EmptyAdminXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<adminTools>
  <users>
  </users>
  <whitelist />
  <blacklist />
  <commands />
  <apitokens />
</adminTools>";

    [Fact]
    public void GetAdmins_ReturnsEmpty_WhenNoAdmins()
    {
        WriteAdminFile(EmptyAdminXml);
        var admins = _service.GetAdmins();
        Assert.Empty(admins);
    }

    [Fact]
    public void GetAdmins_ParsesExistingUsers()
    {
        WriteAdminFile(@"<?xml version=""1.0"" encoding=""UTF-8""?>
<adminTools>
  <users>
    <user platform=""EOS"" userid=""000285d427524db39c4f115689a064c0"" name=""NonToxThicc"" permission_level=""0"" />
    <user platform=""Steam"" userid=""76561198021925107"" name=""TestMod"" permission_level=""100"" />
  </users>
</adminTools>");

        var admins = _service.GetAdmins();
        Assert.Equal(2, admins.Count);

        var nontox = admins.First(a => a.Name == "NonToxThicc");
        Assert.Equal("EOS", nontox.Platform);
        Assert.Equal(0, nontox.PermissionLevel);
        Assert.Equal("Owner", nontox.PermissionLabel);

        var mod = admins.First(a => a.Name == "TestMod");
        Assert.Equal(100, mod.PermissionLevel);
    }

    [Fact]
    public void IsAdmin_MatchesOnUserIdPortionOfPrefixedId()
    {
        WriteAdminFile(@"<?xml version=""1.0""?>
<adminTools>
  <users>
    <user platform=""EOS"" userid=""000285d427524db39c4f115689a064c0"" name=""Test"" permission_level=""0"" />
  </users>
</adminTools>");

        // Telnet gives us prefixed IDs like "EOS_000285..."
        Assert.True(_service.IsAdmin("EOS_000285d427524db39c4f115689a064c0"));
        // Also works without the prefix
        Assert.True(_service.IsAdmin("000285d427524db39c4f115689a064c0"));
        // Different user is not admin
        Assert.False(_service.IsAdmin("EOS_ffffffffffffffffffffffffffffffff"));
    }

    [Fact]
    public void SetAdmin_AddsNewEntry()
    {
        WriteAdminFile(EmptyAdminXml);

        var ok = _service.SetAdmin("Steam", "76561198015490744", "NewAdmin", 0);
        Assert.True(ok);

        var admins = _service.GetAdmins();
        var added = Assert.Single(admins);
        Assert.Equal("NewAdmin", added.Name);
        Assert.Equal("Steam", added.Platform);
        Assert.Equal(0, added.PermissionLevel);
    }

    [Fact]
    public void SetAdmin_UpdatesExistingEntry()
    {
        WriteAdminFile(@"<?xml version=""1.0""?>
<adminTools>
  <users>
    <user platform=""Steam"" userid=""76561198015490744"" name=""Old"" permission_level=""100"" />
  </users>
</adminTools>");

        _service.SetAdmin("Steam", "76561198015490744", "Updated", 0);

        var admins = _service.GetAdmins();
        var updated = Assert.Single(admins);
        Assert.Equal("Updated", updated.Name);
        Assert.Equal(0, updated.PermissionLevel);
    }

    [Fact]
    public void RemoveAdmin_DeletesEntryByUserId()
    {
        WriteAdminFile(@"<?xml version=""1.0""?>
<adminTools>
  <users>
    <user platform=""Steam"" userid=""76561198015490744"" name=""Keep"" permission_level=""100"" />
    <user platform=""Steam"" userid=""76561198015490745"" name=""Remove"" permission_level=""0"" />
  </users>
</adminTools>");

        var ok = _service.RemoveAdmin("76561198015490745");
        Assert.True(ok);

        var admins = _service.GetAdmins();
        var remaining = Assert.Single(admins);
        Assert.Equal("Keep", remaining.Name);
    }

    [Fact]
    public void PermissionLabel_MapsKnownLevels()
    {
        var owner = new AdminUser { PermissionLevel = 0 };
        var admin = new AdminUser { PermissionLevel = 1 };
        var modUser = new AdminUser { PermissionLevel = 2 };
        var custom = new AdminUser { PermissionLevel = 50 };

        Assert.Equal("Owner", owner.PermissionLabel);
        Assert.Equal("Admin", admin.PermissionLabel);
        Assert.Equal("Moderator", modUser.PermissionLabel);
        Assert.Equal("Level 50", custom.PermissionLabel);
    }
}
