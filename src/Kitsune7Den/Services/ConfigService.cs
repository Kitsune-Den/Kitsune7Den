using System.IO;
using System.Xml.Linq;
using Kitsune7Den.Localization;
using Kitsune7Den.Models;

namespace Kitsune7Den.Services;

public class ConfigService
{
    private readonly AppSettings _settings;

    public ConfigService(AppSettings settings)
    {
        _settings = settings;
    }

    private string ConfigPath => Path.Combine(_settings.ServerDirectory, "serverconfig.xml");

    /// <summary>
    /// Scan both built-in and user-generated world folders for valid worlds.
    /// Valid worlds are directories containing a main.ttw file.
    /// </summary>
    public List<string> DiscoverWorlds(string? userDataFolder = null)
    {
        var worlds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        // Built-in worlds ship with the server
        if (!string.IsNullOrEmpty(_settings.ServerDirectory))
        {
            var builtInDir = Path.Combine(_settings.ServerDirectory, "Data", "Worlds");
            if (Directory.Exists(builtInDir))
            {
                foreach (var dir in Directory.GetDirectories(builtInDir))
                {
                    if (File.Exists(Path.Combine(dir, "main.ttw")))
                        worlds.Add(Path.GetFileName(dir));
                }
            }
        }

        // User-generated RWG worlds: the default location, plus UserDataFolder when
        // serverconfig.xml overrides it (the server then writes RWG worlds there)
        var generatedDirs = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "7DaysToDie", "GeneratedWorlds")
        };
        if (!string.IsNullOrWhiteSpace(userDataFolder))
            generatedDirs.Add(Path.Combine(userDataFolder, "GeneratedWorlds"));

        foreach (var generatedDir in generatedDirs.Where(Directory.Exists))
        {
            foreach (var dir in Directory.GetDirectories(generatedDir))
            {
                if (File.Exists(Path.Combine(dir, "main.ttw")))
                    worlds.Add(Path.GetFileName(dir));
            }
        }

        // Always include RWG as a special option (triggers fresh world gen)
        worlds.Add("RWG");

        return worlds.ToList();
    }

    /// <param name="sandboxEra">
    /// True for a 7D2D 3.0+ server. The properties the game now reads from SandboxCode
    /// are then grouped as "Legacy" instead of in their old sections, because editing
    /// them there would do nothing.
    /// </param>
    public List<ServerConfigProperty> LoadConfig(bool sandboxEra = false)
    {
        if (!File.Exists(ConfigPath))
            return [];

        var doc = XDocument.Load(ConfigPath, LoadOptions.PreserveWhitespace);
        var properties = new List<ServerConfigProperty>();

        // Discover worlds once so we can inject into GameWorld options
        var userDataFolder = doc.Descendants("property")
            .FirstOrDefault(e => e.Attribute("name")?.Value == "UserDataFolder")?
            .Attribute("value")?.Value;
        var discoveredWorlds = DiscoverWorlds(userDataFolder);

        foreach (var element in doc.Descendants("property"))
        {
            var name = element.Attribute("name")?.Value ?? "";
            var value = element.Attribute("value")?.Value ?? "";

            if (string.IsNullOrEmpty(name)) continue;

            var prop = FieldDefinitions.GetDefinition(name);
            prop.Value = value;

            // Dynamic: populate GameWorld from scanned folders
            if (name == "GameWorld")
            {
                prop.Options = discoveredWorlds.ToArray();
                prop.OptionLabels = null;
            }

            if (sandboxEra && FieldDefinitions.IsSandboxGoverned(name))
            {
                prop.Category = FieldDefinitions.LegacyCategory;
                prop.IsSandboxGoverned = true;
            }

            properties.Add(prop);
        }

        return properties;
    }

    // ── 7D2D 3.x serverconfig.xml migration ──
    // Ported from KitsuneCommand's ServerConfigService.MigrateConfigTo30.

    /// <summary>
    /// True when serverconfig.xml still has live properties that 3.x ignores (they moved
    /// into SandboxCode), or lacks a SandboxCode property — i.e. migrating would do something.
    /// </summary>
    public bool NeedsSandboxMigration()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return false;
            var names = XDocument.Load(ConfigPath).Descendants("property")
                .Select(p => p.Attribute("name")?.Value ?? "")
                .ToList();
            return names.Any(FieldDefinitions.IsSandboxGoverned) ||
                   !names.Contains("SandboxCode", StringComparer.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Bring serverconfig.xml into the 7D2D 3.x shape:
    ///   1. comment out (never delete) each sandbox-governed property, keeping its old value
    ///      readable in the file;
    ///   2. add SandboxCode, Region and Language if missing, with the game's own template
    ///      defaults (SandboxCode = the default Adventurer preset);
    ///   3. leave everything else untouched.
    /// Takes a timestamped backup first, and is idempotent: a second run changes nothing.
    /// </summary>
    public ConfigMigrationResult MigrateToSandbox()
    {
        if (!File.Exists(ConfigPath))
            throw new FileNotFoundException("serverconfig.xml not found.", ConfigPath);

        var result = new ConfigMigrationResult();
        var doc = XDocument.Load(ConfigPath, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new InvalidDataException("serverconfig.xml has no root element.");

        var governed = root.Descendants("property")
            .Where(p => FieldDefinitions.IsSandboxGoverned(p.Attribute("name")?.Value ?? ""))
            .ToList();
        foreach (var prop in governed)
        {
            var name = prop.Attribute("name")?.Value ?? "";
            var value = prop.Attribute("value")?.Value ?? "";
            // XML comments can't contain "--"
            var body = $" <property name=\"{name}\" value=\"{value}\"/> moved to the in-game Sandbox in 7D2D 3.x (now set by SandboxCode) "
                .Replace("--", "—");
            prop.ReplaceWith(new XComment(body));
            result.CommentedOut.Add(name);
        }

        // New lines go before the whitespace that precedes </ServerSettings>, indented like
        // the existing properties, so the file stays readable for hand edits.
        var indent = (root.Elements("property").FirstOrDefault()?.PreviousNode as XText)?.Value ?? "\n\t";
        var closing = root.LastNode as XText ?? new XText("\n");
        if (closing.Parent is null) root.Add(closing);

        foreach (var (name, value) in FieldDefinitions.SandboxEraAdditions)
        {
            var exists = root.Descendants("property")
                .Any(p => string.Equals(p.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
            if (exists) continue;
            closing.AddBeforeSelf(new XText(indent),
                new XElement("property", new XAttribute("name", name), new XAttribute("value", value)));
            result.Added.Add(name);
        }

        if (!result.Changed)
            return result; // already 3.x-shaped: no backup churn

        result.BackupPath = $"{ConfigPath}.pre-3x-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        File.Copy(ConfigPath, result.BackupPath, overwrite: true);
        doc.Save(ConfigPath);
        return result;
    }

    public bool SaveConfig(List<ServerConfigProperty> properties)
    {
        if (!File.Exists(ConfigPath))
            return false;

        var backupPath = ConfigPath + ".bak";
        File.Copy(ConfigPath, backupPath, overwrite: true);

        var doc = XDocument.Load(ConfigPath, LoadOptions.PreserveWhitespace);

        foreach (var prop in properties)
        {
            var element = doc.Descendants("property")
                .FirstOrDefault(e => e.Attribute("name")?.Value == prop.Name);

            if (element is not null)
            {
                element.SetAttributeValue("value", prop.Value);
            }
        }

        doc.Save(ConfigPath);
        return true;
    }

    public string? GetRawConfig()
    {
        return File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : null;
    }

    public bool SaveRawConfig(string content)
    {
        if (!File.Exists(ConfigPath))
            return false;

        var backupPath = ConfigPath + ".bak";
        File.Copy(ConfigPath, backupPath, overwrite: true);

        // Validate XML before saving
        try
        {
            XDocument.Parse(content);
        }
        catch
        {
            return false;
        }

        File.WriteAllText(ConfigPath, content);
        return true;
    }
}

/// <summary>Outcome of <see cref="ConfigService.MigrateToSandbox"/>.</summary>
public class ConfigMigrationResult
{
    /// <summary>Sandbox-governed properties that were commented out.</summary>
    public List<string> CommentedOut { get; } = [];
    /// <summary>3.x properties that were added (SandboxCode, Region, Language).</summary>
    public List<string> Added { get; } = [];
    /// <summary>Timestamped copy taken before writing, or null when nothing changed.</summary>
    public string? BackupPath { get; set; }
    public bool Changed => CommentedOut.Count > 0 || Added.Count > 0;
}

public static class FieldDefinitions
{
    private static readonly Dictionary<string, ServerConfigProperty> Definitions = new()
    {
        // ── Core ──
        ["ServerName"] = Def("Core", "Display name shown in the server browser", "My 7D2D Server", ConfigFieldType.Text),
        ["ServerPassword"] = Def("Core", "Password required to join (blank = no password)", "", ConfigFieldType.Password),
        ["ServerDescription"] = Def("Core", "Description shown in the server browser listing", "A 7 Days to Die Server", ConfigFieldType.Text),
        ["ServerLoginConfirmationText"] = Def("Core", "Message players must accept before joining", "", ConfigFieldType.Text),
        ["Region"] = Def("Core", "Region the server is in, used by the server browser's region filter (3.x)", "NorthAmericaEast", ConfigFieldType.Select,
            ["NorthAmericaEast", "NorthAmericaWest", "CentralAmerica", "SouthAmerica", "Europe", "Russia", "Asia", "MiddleEast", "Africa", "Oceania"],
            ["North America East", "North America West", "Central America", "South America", "Europe", "Russia", "Asia", "Middle East", "Africa", "Oceania"]),
        ["Language"] = Def("Core", "Primary language of the players, written as its English name (\"Chinese\", not \"中文\") so the server browser search finds it (3.x)", "English", ConfigFieldType.EditableSelect,
            ["English", "Chinese", "German", "Spanish", "French", "Italian", "Japanese", "Korean", "Polish", "Portuguese", "Russian", "Turkish"]),

        // ── Sandbox (3.x) ──
        ["SandboxCode"] = Def("Sandbox", "7D2D 3.x keeps the gameplay rules (difficulty, loot, zombies, blood moons, day length...) in this one code. Use the Sandbox editor, or paste a code copied from the game's Sandbox Options screen.", "AAAJABJACJADJARFBNC", ConfigFieldType.Sandbox),

        // ── World ──
        ["GameWorld"] = Def("World", "Auto-populated from Data/Worlds and GeneratedWorlds. Pick an existing world or type a custom name.", "Navezgane", ConfigFieldType.EditableSelect,
            ["Navezgane", "RWG"]),
        ["WorldGenSeed"] = Def("World", "Seed string used for random world generation", "SomeSeed", ConfigFieldType.Text),
        ["WorldGenSize"] = Def("World", "RWG map width/height in meters. Officially supported: 6144 to 10240, a multiple of 2048.", "6144", ConfigFieldType.EditableSelect,
            ["6144", "8192", "10240"]),
        ["GameName"] = Def("World", "Save game name (letters, digits, _ - . and spaces). Changing this starts a new save.", "My Game", ConfigFieldType.Text),
        ["GameMode"] = Def("World", "Game mode for the server", "GameModeSurvival", ConfigFieldType.Select,
            ["GameModeSurvival"]),
        ["BedrollDeadZoneSize"] = Def("World", "Radius in blocks where others can't place bedrolls near yours", "15", ConfigFieldType.Number),
        ["BedrollExpiryTime"] = Def("World", "Real-world days a bedroll stays active after owner was last online", "45", ConfigFieldType.Number),
        ["MaxUncoveredMapChunksPerPlayer"] = Def("World", "Max map chunks revealed per player (131072 = full map)", "131072", ConfigFieldType.Number),

        // ── Block Damage ──
        ["BlockDamagePlayer"] = Def("Block Damage", "Scales how much damage players deal to blocks (percentage). 300% is popular for solo play to reduce resource grind.", "100", ConfigFieldType.Select,
            ["25", "50", "75", "100", "125", "150", "175", "200", "300"],
            ["25%", "50%", "75%", "100%", "125%", "150%", "175%", "200%", "300%"]),
        ["BlockDamageAI"] = Def("Block Damage", "Scales zombie/animal damage to blocks during normal play. Lower to 25-50% to prevent zombies clawing through walls.", "100", ConfigFieldType.Select,
            ["25", "50", "75", "100", "125", "150", "175", "200", "300"],
            ["25%", "50%", "75%", "100%", "125%", "150%", "175%", "200%", "300%"]),
        ["BlockDamageAIBM"] = Def("Block Damage", "Scales zombie damage to blocks during Blood Moon only. Separate from normal AI damage so hordes can hit harder (or softer).", "100", ConfigFieldType.Select,
            ["25", "50", "75", "100", "125", "150", "175", "200", "300"],
            ["25%", "50%", "75%", "100%", "125%", "150%", "175%", "200%", "300%"]),

        // ── Gameplay ──
        ["GameDifficulty"] = Def("Gameplay", "Overall difficulty. On 3.x the Sandbox difficulty preset is what players actually feel; this one is mostly legacy.", "2", ConfigFieldType.Select,
            ["0", "1", "2", "3", "4", "5"],
            ["0 - Scavenger", "1 - Adventurer", "2 - Nomad", "3 - Warrior", "4 - Survivalist", "5 - Insane"]),
        ["DayNightLength"] = Def("Gameplay", "Real-time minutes per in-game 24h cycle", "60", ConfigFieldType.Number),
        ["DayLightLength"] = Def("Gameplay", "In-game hours of daylight (out of 24)", "18", ConfigFieldType.Number),
        ["BuildCreate"] = Def("Gameplay", "Allow creative/cheat mode building", "false", ConfigFieldType.Select,
            ["true", "false"], ["On", "Off"]),
        ["BloodMoonFrequency"] = Def("Gameplay", "Blood moon horde every N days", "7", ConfigFieldType.Number),
        ["BloodMoonRange"] = Def("Gameplay", "Random +/- days added to blood moon schedule (0 = exact)", "0", ConfigFieldType.Number),
        ["BloodMoonWarning"] = Def("Gameplay", "Hours before blood moon the warning appears", "8", ConfigFieldType.Number),
        ["BiomeProgression"] = Def("Gameplay", "Zombies get harder in further biomes", "true", ConfigFieldType.Select,
            ["true", "false"], ["On", "Off"]),
        ["StormFreq"] = Def("Gameplay", "Weather storm frequency. 0% turns them off.", "50", ConfigFieldType.Select,
            ["0", "50", "100", "150", "200", "300", "400", "500"],
            ["0% - Off", "50%", "100%", "150%", "200%", "300%", "400%", "500%"]),
        ["QuestProgressionDailyLimit"] = Def("Gameplay", "Max quests per day that count toward tier progression. Extra quests still give rewards. Set to 0 for unlimited.", "10", ConfigFieldType.Number),
        ["BloodMoonEnemyCount"] = Def("Gameplay", "Zombies alive per player during blood moon. Game stage and MaxSpawnedZombies can override this. High impact on performance.", "8", ConfigFieldType.Number),

        // ── Player ──
        ["XPMultiplier"] = Def("Player", "XP gain multiplier (%). Higher values = faster leveling.", "100", ConfigFieldType.Select,
            ["25", "50", "75", "100", "125", "150", "175", "200", "300"],
            ["25%", "50%", "75%", "100%", "125%", "150%", "175%", "200%", "300%"]),
        ["PlayerSafeZoneLevel"] = Def("Player", "Player level at which the safe zone protection ends. New players are protected from zombies until this level.", "5", ConfigFieldType.Number),
        ["PlayerSafeZoneHours"] = Def("Player", "Hours of safe zone protection for new players. After this time, protection ends regardless of level.", "5", ConfigFieldType.Number),
        ["PlayerKillingMode"] = Def("Player", "PvP rules for player-vs-player combat", "3", ConfigFieldType.Select,
            ["0", "1", "2", "3"],
            ["0 - No Killing", "1 - Kill Allies Only", "2 - Kill Strangers Only", "3 - Kill Everyone"]),
        ["PartySharedKillRange"] = Def("Player", "Distance in meters for party members to share XP from kills. 0 = must be very close.", "100", ConfigFieldType.Number),
        ["DeathPenalty"] = Def("Player", "XP penalty when a player dies", "1", ConfigFieldType.Select,
            ["0", "1", "2", "3"],
            ["0 - None", "1 - Default", "2 - Injured", "3 - Permanent Death"]),
        ["AllowSpawnNearFriend"] = Def("Player", "Can new players select to join near a friend on first connect?", "2", ConfigFieldType.Select,
            ["0", "1", "2"],
            ["0 - Disabled", "1 - Always", "2 - Only Near Friends in Forest"]),
        ["JarRefund"] = Def("Player", "Percentage of glass jars returned when crafting food/drink (0 = no return)", "0", ConfigFieldType.Select,
            ["0", "5", "10", "15", "20", "25", "30", "35", "40", "45", "50", "55", "60", "65", "70", "75", "80", "85", "90", "95", "100"],
            ["0%", "5%", "10%", "15%", "20%", "25%", "30%", "35%", "40%", "45%", "50%", "55%", "60%", "65%", "70%", "75%", "80%", "85%", "90%", "95%", "100%"]),

        // ── Zombies ──
        ["ZombieMove"] = Def("Zombies", "Daytime zombie movement speed", "0", ConfigFieldType.Select,
            ["0", "1", "2", "3", "4"],
            ["0 - Walk", "1 - Slow", "2 - Jog", "3 - Run", "4 - Sprint"]),
        ["ZombieMoveNight"] = Def("Zombies", "Nighttime zombie movement speed", "3", ConfigFieldType.Select,
            ["0", "1", "2", "3", "4"],
            ["0 - Walk", "1 - Slow", "2 - Jog", "3 - Run", "4 - Sprint"]),
        ["ZombieFeralMove"] = Def("Zombies", "Feral zombie movement speed", "3", ConfigFieldType.Select,
            ["0", "1", "2", "3", "4"],
            ["0 - Walk", "1 - Slow", "2 - Jog", "3 - Run", "4 - Sprint"]),
        ["ZombieBMMove"] = Def("Zombies", "Blood moon zombie movement speed", "3", ConfigFieldType.Select,
            ["0", "1", "2", "3", "4"],
            ["0 - Walk", "1 - Slow", "2 - Jog", "3 - Run", "4 - Sprint"]),
        ["EnemyDifficulty"] = Def("Zombies", "Feral adds more challenging zombie types", "0", ConfigFieldType.Select,
            ["0", "1"], ["0 - Normal", "1 - Feral"]),
        ["EnemySpawnMode"] = Def("Zombies", "Control whether enemies spawn in the world. Turn off for peaceful building or testing.", "true", ConfigFieldType.Select,
            ["true", "false"], ["Yes", "No"]),
        ["MaxSpawnedZombies"] = Def("Zombies", "Max alive zombies in the whole world (scaled x1.9 on blood moons, x2.1 for sleepers). Big performance impact.", "64", ConfigFieldType.Number),
        ["MaxSpawnedAnimals"] = Def("Zombies", "Max alive animals at once", "50", ConfigFieldType.Number),
        ["ZombieFeralSense"] = Def("Zombies", "Feral zombies can sense players through walls and obstacles", "0", ConfigFieldType.Select,
            ["0", "1", "2", "3"],
            ["0 - Off", "1 - Day Only", "2 - Night Only", "3 - All"]),
        ["AISmellMode"] = Def("Zombies", "How fast zombies move when tracking players by scent. Zombies can smell blood, food, and forges. Higher speeds make cooking and crafting near zombies much more dangerous.", "3", ConfigFieldType.Select,
            ["0", "1", "2", "3", "4", "5"],
            ["0 - Off", "1 - Walk", "2 - Jog", "3 - Run", "4 - Sprint", "5 - Nightmare"]),

        // ── Loot & Drops ──
        ["LootAbundance"] = Def("Loot & Drops", "Loot quantity multiplier (%)", "100", ConfigFieldType.Select,
            ["25", "50", "75", "100", "125", "150", "175", "200", "300"],
            ["25%", "50%", "75%", "100%", "125%", "150%", "175%", "200%", "300%"]),
        ["LootRespawnDays"] = Def("Loot & Drops", "Days before looted containers respawn", "7", ConfigFieldType.Number),
        ["AirDropFrequency"] = Def("Loot & Drops", "Air drop interval in in-game hours (0 = disabled)", "72", ConfigFieldType.Number),
        ["AirDropMarker"] = Def("Loot & Drops", "Show or hide the map/compass marker when an air drop lands", "true", ConfigFieldType.Select,
            ["true", "false"], ["Show", "Hide"]),
        ["DropOnDeath"] = Def("Loot & Drops", "What players drop when killed", "1", ConfigFieldType.Select,
            ["0", "1", "2", "3", "4"],
            ["0 - Nothing", "1 - Everything", "2 - Toolbelt Only", "3 - Backpack Only", "4 - Delete All"]),
        ["DropOnQuit"] = Def("Loot & Drops", "What players drop when disconnecting", "1", ConfigFieldType.Select,
            ["0", "1", "2", "3"],
            ["0 - Nothing", "1 - Everything", "2 - Toolbelt Only", "3 - Backpack Only"]),

        // ── Land Claims ──
        ["LandClaimSize"] = Def("Land Claims", "Protected area size in blocks (41 = 20 blocks each direction)", "41", ConfigFieldType.Number),
        ["LandClaimDeadZone"] = Def("Land Claims", "Minimum distance between land claims in blocks", "30", ConfigFieldType.Number),
        ["LandClaimExpiryTime"] = Def("Land Claims", "Real-world days a player can be offline before their claims expire", "7", ConfigFieldType.Number),
        ["LandClaimDecayMode"] = Def("Land Claims", "How an offline player's land claim protection decays", "0", ConfigFieldType.Select,
            ["0", "1", "2"], ["0 - Slow (Linear)", "1 - Fast (Exponential)", "2 - None (full until expiry)"]),
        ["LandClaimOnlineDurabilityModifier"] = Def("Land Claims", "Block hardness multiplier while the owner is online (0 = indestructible)", "4", ConfigFieldType.Number),
        ["LandClaimOfflineDurabilityModifier"] = Def("Land Claims", "Block hardness multiplier while the owner is offline (0 = indestructible)", "4", ConfigFieldType.Number),
        ["LandClaimCount"] = Def("Land Claims", "Maximum allowed land claims per player", "5", ConfigFieldType.Number),
        ["LandClaimOfflineDelay"] = Def("Land Claims", "Minutes after logout before land claim switches from online to offline hardness", "0", ConfigFieldType.Number),

        // ── Network ──
        ["ServerPort"] = Def("Network", "Main game port (also uses +1 and +2 for auxiliary connections)", "26900", ConfigFieldType.Number),
        ["ServerVisibility"] = Def("Network", "Who can see this server in the server browser", "2", ConfigFieldType.Select,
            ["0", "1", "2"], ["0 - Not Listed", "1 - Friends Only", "2 - Public"]),
        ["ServerMaxPlayerCount"] = Def("Network", "Maximum concurrent players", "8", ConfigFieldType.Number),
        ["ServerReservedSlots"] = Def("Network", "Of the max player count, this many slots are kept for players with the reserved-slot permission level", "0", ConfigFieldType.Number),
        ["ServerMaxWorldTransferSpeedKiBs"] = Def("Network", "Max world download speed per new client in KiB/s (the game caps it near 1300)", "512", ConfigFieldType.Number),
        ["ServerDisabledNetworkProtocols"] = Def("Network", "Protocols to disable. Dedicated servers should disable SteamNetworking if there's no NAT router or port-forwarding is set up correctly.", "SteamNetworking", ConfigFieldType.Select,
            ["", "SteamNetworking", "LiteNetLib", "SteamNetworking,LiteNetLib"],
            ["None", "SteamNetworking", "LiteNetLib", "Both"]),

        // ── Admin ──
        ["TelnetEnabled"] = Def("Admin", "Enable the telnet remote console", "true", ConfigFieldType.Select,
            ["true", "false"], ["Yes", "No"]),
        ["TelnetPort"] = Def("Admin", "Port for telnet connections", "8081", ConfigFieldType.Number),
        ["TelnetPassword"] = Def("Admin", "Password for telnet access. If blank, telnet only listens on this machine (localhost).", "", ConfigFieldType.Password),
        ["TelnetFailedLoginLimit"] = Def("Admin", "Failed login attempts before temporary ban", "10", ConfigFieldType.Number),
        ["TelnetFailedLoginsBlocktime"] = Def("Admin", "Seconds to block after too many failed telnet logins", "10", ConfigFieldType.Number),
        ["EACEnabled"] = Def("Admin", "Easy Anti-Cheat — disabling allows modded clients to connect", "true", ConfigFieldType.Select,
            ["true", "false"], ["On", "Off"]),
        ["HideCommandExecutionLog"] = Def("Admin", "Hide logging of command execution. Higher values hide from more sources.", "0", ConfigFieldType.Select,
            ["0", "1", "2", "3"],
            ["0 - Show All", "1 - Hide from Telnet/Panel", "2 - Also Hide from Clients", "3 - Hide Everything"]),
        ["AdminFileName"] = Def("Admin", "Server admin file name. Path relative to saves folder.", "serveradmin.xml", ConfigFieldType.Text),
        ["ServerAdminSlots"] = Def("Admin", "This many admins can still join when the server is full", "0", ConfigFieldType.Number),
        ["ServerAdminSlotsPermission"] = Def("Admin", "Permission level required to use admin slots (0 = highest admin)", "0", ConfigFieldType.Number),
        ["ServerReservedSlotsPermission"] = Def("Admin", "Permission level required to use reserved slots", "100", ConfigFieldType.Number),
        ["IgnoreEOSSanctions"] = Def("Admin", "Ignore EOS sanctions when allowing players to join", "false", ConfigFieldType.Select,
            ["true", "false"], ["Yes", "No"]),
        ["PersistentPlayerProfiles"] = Def("Admin", "If enabled, players always join with the last profile they used. If disabled, they can pick any profile.", "false", ConfigFieldType.Select,
            ["true", "false"], ["Yes", "No"]),
        ["ServerAllowCrossplay"] = Def("Network", "Enable crossplay between platforms. Crossplay servers must not ignore sanctions and use default or fewer player slots.", "false", ConfigFieldType.Select,
            ["true", "false"], ["On", "Off"]),
        ["CameraRestrictionMode"] = Def("Admin", "Restrict which camera perspective players can use", "0", ConfigFieldType.Select,
            ["0", "1", "2"],
            ["0 - Free (1st & 3rd)", "1 - First Person Only", "2 - Third Person Only"]),
        ["TwitchServerPermission"] = Def("Admin", "Permission level required to use Twitch integration on the server", "90", ConfigFieldType.Number),
        ["TwitchBloodMoonAllowed"] = Def("Admin", "Allow Twitch actions during blood moon. Can cause lag from extra zombie spawns.", "false", ConfigFieldType.Select,
            ["true", "false"], ["Yes", "No"]),

        // ── Advanced ──
        ["ServerWebsiteURL"] = Def("Advanced", "URL shown in the server browser as the server's website", "", ConfigFieldType.Text),
        ["ServerMaxAllowedViewDistance"] = Def("Advanced", "Max view distance a client may request (6-12). High impact on memory and performance.", "12", ConfigFieldType.Number),
        ["TerminalWindowEnabled"] = Def("Advanced", "Show a terminal window for log output and commands (Windows only)", "true", ConfigFieldType.Select,
            ["true", "false"], ["Yes", "No"]),
        ["WebDashboardEnabled"] = Def("Advanced", "Enable the built-in 7D2D web dashboard", "false", ConfigFieldType.Select,
            ["true", "false"], ["Yes", "No"]),
        ["WebDashboardPort"] = Def("Advanced", "Port for the built-in web dashboard", "8080", ConfigFieldType.Number),
        ["WebDashboardUrl"] = Def("Advanced", "External dashboard URL for reverse proxy setups", "", ConfigFieldType.Text),
        ["EnableMapRendering"] = Def("Advanced", "Render the map to tile images while exploring. Used by the web dashboard to display the map.", "false", ConfigFieldType.Select,
            ["true", "false"], ["Yes", "No"]),
        ["DynamicMeshEnabled"] = Def("Advanced", "Enable the dynamic mesh system for improved visuals", "true", ConfigFieldType.Select,
            ["true", "false"], ["Yes", "No"]),
        ["DynamicMeshLandClaimOnly"] = Def("Advanced", "Only use dynamic mesh in land claim areas", "true", ConfigFieldType.Select,
            ["true", "false"], ["Yes", "No"]),
        ["DynamicMeshLandClaimBuffer"] = Def("Advanced", "Dynamic mesh land claim chunk radius", "3", ConfigFieldType.Number),
        ["DynamicMeshMaxItemCache"] = Def("Advanced", "Max items processed concurrently. Higher values use more RAM.", "3", ConfigFieldType.Number),
        ["MaxChunkAge"] = Def("Advanced", "In-game days before unvisited/unprotected chunks reset. -1 = never reset.", "-1", ConfigFieldType.Number),
        ["SaveDataLimit"] = Def("Advanced", "Max disk space per save in MB. Chunks may be reset to free space. -1 = no limit.", "-1", ConfigFieldType.Number),
        ["MaxQueuedMeshLayers"] = Def("Advanced", "Max chunk mesh layers queued during generation. Lower = less memory, slower generation.", "1000", ConfigFieldType.Number),
        ["UserDataFolder"] = Def("Advanced", "Override path for all user data, saves, and RWG worlds. Leave blank for default.", "", ConfigFieldType.Text),
    };

    /// <summary>
    /// Ordered list of categories matching KitsuneDen's layout.
    /// </summary>
    public static readonly string[] CategoryOrder =
    [
        "Core", "Sandbox", "World", "Block Damage", "Gameplay", "Player", "Zombies",
        "Loot & Drops", "Land Claims", "Network", "Admin", "Advanced", LegacyCategory
    ];

    /// <summary>Where sandbox-governed properties go on a 3.x server.</summary>
    public const string LegacyCategory = "Legacy";

    /// <summary>
    /// serverconfig.xml properties that 7D2D 3.x reads from SandboxCode instead. On a 3.x
    /// server whatever serverconfig says for these is ignored.
    ///
    /// This is KitsuneCommand's list, derived from the game assembly rather than patch
    /// notes: EnumGamePrefs names that also exist in SandboxOptions (the game's own
    /// sandbox-link test), plus the two prefs the sandbox apply step overwrites from a
    /// differently named option (BlockDamagePlayer from BlockDamage, LootAbundance from
    /// GlobalLootCount). Re-verified against 3.3 b18. Survivors that stay live in
    /// serverconfig include GameDifficulty, EnemyDifficulty, MaxSpawnedZombies/Animals,
    /// all LandClaim*, PlayerSafeZone* and BedrollDeadZoneSize.
    /// </summary>
    public static readonly IReadOnlySet<string> SandboxGovernedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "DeathPenalty", "DropOnDeath", "DropOnQuit", "DayNightLength", "DayLightLength",
        "QuestProgressionDailyLimit", "JarRefund", "BiomeProgression", "StormFreq",
        "BlockDamageAI", "BlockDamageAIBM", "XPMultiplier", "EnemySpawnMode",
        "ZombieFeralSense", "ZombieMove", "ZombieMoveNight", "ZombieFeralMove",
        "ZombieBMMove", "AISmellMode", "BloodMoonFrequency", "BloodMoonRange",
        "BloodMoonWarning", "BloodMoonEnemyCount", "LootRespawnDays", "AirDropFrequency",
        "AirDropMarker", "LootTimer",
        "BlockDamagePlayer", "LootAbundance",
    };

    public static bool IsSandboxGoverned(string name) => SandboxGovernedKeys.Contains(name);

    /// <summary>Properties the 3.x template has that older configs lack, with the template's values.</summary>
    public static readonly (string Name, string Value)[] SandboxEraAdditions =
    [
        ("SandboxCode", "AAAJABJACJADJARFBNC"),
        ("Region", "NorthAmericaEast"),
        ("Language", "English"),
    ];

    /// <summary>
    /// A fresh property carrying the definition for <paramref name="name"/>, with label,
    /// description and option labels in the active UI language. English lives here; other
    /// languages come from Strings.&lt;lang&gt;.resx (Cfg_&lt;Name&gt;_Name/_Desc/_Opts) and fall
    /// back to English per field.
    /// </summary>
    public static ServerConfigProperty GetDefinition(string name)
    {
        var prop = Definitions.TryGetValue(name, out var def)
            ? new ServerConfigProperty
            {
                Name = name,
                Category = def.Category,
                Description = def.Description,
                DefaultValue = def.DefaultValue,
                FieldType = def.FieldType,
                Options = def.Options,
                OptionLabels = def.OptionLabels
            }
            : new ServerConfigProperty
            {
                Name = name,
                Category = "Other",
                Description = "",
                DefaultValue = "",
                FieldType = ConfigFieldType.Text
            };

        Localize(prop);
        return prop;
    }

    /// <summary>Re-apply display text for the active language (keeps Value and Category).</summary>
    public static void Localize(ServerConfigProperty prop)
    {
        Definitions.TryGetValue(prop.Name, out var def);

        prop.Label = Loc.TryGet($"Cfg_{prop.Name}_Name");
        prop.Description = Loc.TryGet($"Cfg_{prop.Name}_Desc") ?? def?.Description ?? "";

        if (prop.Name == "GameWorld" || prop.Options is null) return;
        var labels = Loc.TryGet($"Cfg_{prop.Name}_Opts")?.Split('|');
        prop.OptionLabels = labels is not null && labels.Length == prop.Options.Length
            ? labels
            : def?.OptionLabels;
    }

    /// <summary>All defined property names (for translation coverage tests).</summary>
    public static IEnumerable<string> DefinedNames => Definitions.Keys;

    /// <summary>A category's heading in the active language. Keys are English ("Loot &amp; Drops").</summary>
    public static string CategoryLabel(string category) =>
        Loc.TryGet("CfgCat_" + CategoryKey(category)) ?? category;

    public static string CategoryKey(string category) =>
        new(category.Where(char.IsLetterOrDigit).ToArray());

    private static ServerConfigProperty Def(string category, string description, string defaultValue,
        ConfigFieldType type, string[]? options = null, string[]? optionLabels = null) =>
        new()
        {
            Category = category,
            Description = description,
            DefaultValue = defaultValue,
            FieldType = type,
            Options = options,
            OptionLabels = optionLabels
        };
}
