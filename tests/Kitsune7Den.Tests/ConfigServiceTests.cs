using System.IO;
using System.Xml.Linq;
using Kitsune7Den.Localization;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.Tests;

/// <summary>
/// Tests the serverconfig.xml parser, the world discovery scanner and the 3.x migration.
/// </summary>
[Collection("Localization")]
public class ConfigServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppSettings _settings;
    private readonly ConfigService _service;

    public ConfigServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Kitsune7DenTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _settings = new AppSettings { ServerDirectory = _tempRoot };
        _service = new ConfigService(_settings);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best effort */ }
    }

    private void WriteServerConfig(string xml) =>
        File.WriteAllText(Path.Combine(_tempRoot, "serverconfig.xml"), xml);

    [Fact]
    public void LoadConfig_ReturnsEmptyList_WhenFileMissing()
    {
        var props = _service.LoadConfig();
        Assert.Empty(props);
    }

    [Fact]
    public void LoadConfig_ParsesPropertiesFromXml()
    {
        WriteServerConfig(@"<?xml version=""1.0""?>
<ServerSettings>
  <property name=""ServerName"" value=""Test Server"" />
  <property name=""ServerMaxPlayerCount"" value=""8"" />
  <property name=""GameWorld"" value=""Navezgane"" />
</ServerSettings>");

        var props = _service.LoadConfig();
        Assert.Equal(3, props.Count);

        var name = props.First(p => p.Name == "ServerName");
        Assert.Equal("Test Server", name.Value);
        Assert.Equal("Core", name.Category);

        var maxPlayers = props.First(p => p.Name == "ServerMaxPlayerCount");
        Assert.Equal("8", maxPlayers.Value);
        Assert.Equal("Network", maxPlayers.Category);
    }

    [Fact]
    public void LoadConfig_EnrichesWithFieldDefinitions()
    {
        WriteServerConfig(@"<?xml version=""1.0""?>
<ServerSettings>
  <property name=""GameDifficulty"" value=""2"" />
</ServerSettings>");

        var props = _service.LoadConfig();
        var diff = Assert.Single(props);
        Assert.Equal("Gameplay", diff.Category);
        Assert.Equal(ConfigFieldType.Select, diff.FieldType);
        Assert.NotNull(diff.Options);
        Assert.Contains("2", diff.Options);
    }

    [Fact]
    public void LoadConfig_UnknownPropertyFallsIntoOtherCategory()
    {
        WriteServerConfig(@"<?xml version=""1.0""?>
<ServerSettings>
  <property name=""SomeCustomModProperty"" value=""xyz"" />
</ServerSettings>");

        var props = _service.LoadConfig();
        var prop = Assert.Single(props);
        Assert.Equal("Other", prop.Category);
        Assert.Equal(ConfigFieldType.Text, prop.FieldType);
    }

    [Fact]
    public void SaveConfig_CreatesBackupFile()
    {
        WriteServerConfig(@"<?xml version=""1.0""?>
<ServerSettings>
  <property name=""ServerName"" value=""Original"" />
</ServerSettings>");

        var props = _service.LoadConfig();
        props[0].Value = "Updated";
        var ok = _service.SaveConfig(props);

        Assert.True(ok);
        Assert.True(File.Exists(Path.Combine(_tempRoot, "serverconfig.xml.bak")));

        // Reload and verify the new value is written
        var reloaded = _service.LoadConfig();
        Assert.Equal("Updated", reloaded[0].Value);

        // And the backup still has the original
        var bak = File.ReadAllText(Path.Combine(_tempRoot, "serverconfig.xml.bak"));
        Assert.Contains("Original", bak);
    }

    [Fact]
    public void DiscoverWorlds_AlwaysIncludesRWG()
    {
        var worlds = _service.DiscoverWorlds();
        Assert.Contains("RWG", worlds);
    }

    [Fact]
    public void DiscoverWorlds_FindsBuiltInWorldsWithMainTtw()
    {
        var worldsDir = Path.Combine(_tempRoot, "Data", "Worlds");
        Directory.CreateDirectory(Path.Combine(worldsDir, "Navezgane"));
        Directory.CreateDirectory(Path.Combine(worldsDir, "PregenTest"));
        Directory.CreateDirectory(Path.Combine(worldsDir, "NotAWorld")); // no main.ttw

        File.WriteAllText(Path.Combine(worldsDir, "Navezgane", "main.ttw"), "");
        File.WriteAllText(Path.Combine(worldsDir, "PregenTest", "main.ttw"), "");

        var worlds = _service.DiscoverWorlds();
        Assert.Contains("Navezgane", worlds);
        Assert.Contains("PregenTest", worlds);
        Assert.DoesNotContain("NotAWorld", worlds);
    }

    [Fact]
    public void GetRawConfig_ReturnsFileContents()
    {
        const string xml = @"<?xml version=""1.0""?>
<ServerSettings>
  <property name=""ServerName"" value=""Test"" />
</ServerSettings>";
        WriteServerConfig(xml);

        var raw = _service.GetRawConfig();
        Assert.NotNull(raw);
        Assert.Contains("ServerName", raw);
    }

    [Fact]
    public void SaveRawConfig_RejectsInvalidXml()
    {
        WriteServerConfig("<?xml version=\"1.0\"?><ServerSettings></ServerSettings>");
        var ok = _service.SaveRawConfig("this is not xml at all <unclosed");
        Assert.False(ok);
    }

    // ── 7D2D 3.x ──

    private const string PreThreeConfig = @"<?xml version=""1.0""?>
<ServerSettings>
  <property name=""ServerName"" value=""Den"" />
  <property name=""GameDifficulty"" value=""3"" />
  <property name=""XPMultiplier"" value=""200"" />
  <property name=""BloodMoonFrequency"" value=""14"" />
  <property name=""LootAbundance"" value=""150"" />
  <property name=""LandClaimSize"" value=""41"" />
</ServerSettings>";

    private string ConfigPath => Path.Combine(_tempRoot, "serverconfig.xml");

    private static List<string> LiveNames(string path) =>
        XDocument.Load(path).Descendants("property").Select(p => p.Attribute("name")!.Value).ToList();

    [Fact]
    public void LoadConfig_SandboxEra_MovesGovernedKeysToLegacy()
    {
        WriteServerConfig(PreThreeConfig);

        var props = _service.LoadConfig(sandboxEra: true);

        Assert.Equal(FieldDefinitions.LegacyCategory, props.Single(p => p.Name == "XPMultiplier").Category);
        Assert.True(props.Single(p => p.Name == "BloodMoonFrequency").IsSandboxGoverned);
        // Survivors stay where they were
        Assert.Equal("Gameplay", props.Single(p => p.Name == "GameDifficulty").Category);
        Assert.Equal("Land Claims", props.Single(p => p.Name == "LandClaimSize").Category);
    }

    [Fact]
    public void LoadConfig_Pre30_LeavesCategoriesAlone()
    {
        WriteServerConfig(PreThreeConfig);
        var props = _service.LoadConfig();
        Assert.Equal("Player", props.Single(p => p.Name == "XPMultiplier").Category);
        Assert.DoesNotContain(props, p => p.IsSandboxGoverned);
    }

    [Fact]
    public void MigrateToSandbox_CommentsOutGovernedKeys_AndAddsThe3xProperties()
    {
        WriteServerConfig(PreThreeConfig);
        Assert.True(_service.NeedsSandboxMigration());

        var result = _service.MigrateToSandbox();

        Assert.True(result.Changed);
        Assert.Equal(["XPMultiplier", "BloodMoonFrequency", "LootAbundance"], result.CommentedOut);
        Assert.Equal(["SandboxCode", "Region", "Language"], result.Added);

        var names = LiveNames(ConfigPath);
        Assert.DoesNotContain("XPMultiplier", names);
        Assert.Contains("GameDifficulty", names);
        Assert.Contains("SandboxCode", names);

        // The old values are preserved as comments, not deleted
        var text = File.ReadAllText(ConfigPath);
        Assert.Contains(@"name=""BloodMoonFrequency"" value=""14""", text);
        Assert.Contains("AAAJABJACJADJARFBNC", text);

        // And the original was backed up first
        Assert.NotNull(result.BackupPath);
        Assert.Contains(@"<property name=""XPMultiplier"" value=""200"" />", File.ReadAllText(result.BackupPath!));
        Assert.False(_service.NeedsSandboxMigration());

        // Added properties sit on their own lines, indented like the rest
        Assert.Contains("\n  <property name=\"Region\"", text);
        Assert.EndsWith("\n</ServerSettings>", text.TrimEnd());
    }

    [Fact]
    public void MigrateToSandbox_IsIdempotent()
    {
        WriteServerConfig(PreThreeConfig);
        _service.MigrateToSandbox();
        var after = File.ReadAllText(ConfigPath);

        var second = _service.MigrateToSandbox();

        Assert.False(second.Changed);
        Assert.Null(second.BackupPath);
        Assert.Equal(after, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void MigrateToSandbox_KeepsAnExistingSandboxCode()
    {
        WriteServerConfig(@"<?xml version=""1.0""?>
<ServerSettings>
  <property name=""SandboxCode"" value=""AAAKABKARDBNB"" />
  <property name=""DropOnDeath"" value=""2"" />
</ServerSettings>");

        var result = _service.MigrateToSandbox();

        Assert.DoesNotContain("SandboxCode", result.Added);
        Assert.Contains(@"value=""AAAKABKARDBNB""", File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void Shipped33Template_NeedsNoMigration_AndHasNoGovernedKeys()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "serverconfig-3.3.xml"), ConfigPath);

        Assert.False(_service.NeedsSandboxMigration());
        var props = _service.LoadConfig(sandboxEra: true);
        Assert.DoesNotContain(props, p => p.IsSandboxGoverned);
        Assert.Equal(ConfigFieldType.Sandbox, props.Single(p => p.Name == "SandboxCode").FieldType);
        // Every property in the official template has a definition (none fall into "Other")
        Assert.DoesNotContain(props, p => p.Category == "Other");
    }

    [Fact]
    public void FieldLabels_MatchTheGamesOwnComments()
    {
        // serverconfig.xml: "0 = No Killing, 1 = Kill Allies Only, 2 = Kill Strangers Only, 3 = Kill Everyone"
        var pvp = FieldDefinitions.GetDefinition("PlayerKillingMode");
        Assert.Equal("1 - Kill Allies Only", pvp.OptionLabels![1]);
        Assert.Equal("2 - Kill Strangers Only", pvp.OptionLabels![2]);

        // "0=Slow (Linear), 1=Fast (Exponential), 2=None"
        var decay = FieldDefinitions.GetDefinition("LandClaimDecayMode");
        Assert.StartsWith("0 - Slow", decay.OptionLabels![0]);
        Assert.StartsWith("2 - None", decay.OptionLabels![2]);
    }

    [Fact]
    public void DiscoverWorlds_AlsoScansUserDataFolder()
    {
        var udf = Path.Combine(_tempRoot, "UserData");
        Directory.CreateDirectory(Path.Combine(udf, "GeneratedWorlds", "Funny Fox Hills"));
        File.WriteAllText(Path.Combine(udf, "GeneratedWorlds", "Funny Fox Hills", "main.ttw"), "");

        Assert.Contains("Funny Fox Hills", _service.DiscoverWorlds(udf));
    }

    [Fact]
    public void Chinese_TranslatesLabelsButNeverValues()
    {
        WriteServerConfig(@"<?xml version=""1.0""?>
<ServerSettings>
  <property name=""PlayerKillingMode"" value=""1"" />
</ServerSettings>");
        try
        {
            Loc.Instance.SetLanguage(Loc.SimplifiedChinese);
            var prop = Assert.Single(_service.LoadConfig());

            Assert.Equal("玩家杀戮", prop.DisplayName);
            Assert.Equal("  (PlayerKillingMode)", prop.KeyHint);
            Assert.Equal("1 - 只能杀死盟友", prop.SelectedOption!.Label);
            Assert.Equal("1", prop.SelectedOption.Value);
            Assert.Equal("Player", prop.Category);
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    }

    [Fact]
    public void EveryField_HasAChineseNameAndDescription_AndMatchingOptionCounts()
    {
        try
        {
            Loc.Instance.SetLanguage(Loc.SimplifiedChinese);
            foreach (var name in FieldDefinitions.DefinedNames)
            {
                Assert.False(string.IsNullOrWhiteSpace(Loc.TryGet($"Cfg_{name}_Name")), $"{name}: no zh name");
                Assert.False(string.IsNullOrWhiteSpace(Loc.TryGet($"Cfg_{name}_Desc")), $"{name}: no zh description");

                var opts = Loc.TryGet($"Cfg_{name}_Opts");
                if (opts is not null)
                    Assert.Equal(FieldDefinitions.GetDefinition(name).Options!.Length, opts.Split('|').Length);
            }
            foreach (var category in FieldDefinitions.CategoryOrder.Append("Other"))
                Assert.NotNull(Loc.TryGet("CfgCat_" + FieldDefinitions.CategoryKey(category)));
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    }
}
