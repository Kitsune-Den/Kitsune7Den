using System.IO;
using System.Xml.Linq;

namespace Kitsune7Den.Services;

/// <summary>A port + password to try, and where they came from.</summary>
public sealed record TelnetEndpoint(int Port, string Password, bool FromServerConfig);

/// <summary>
/// Works out which telnet port and password to use. Kitsune7Den always connects to
/// 127.0.0.1, i.e. the server whose serverconfig.xml sits in ServerDirectory, so that file
/// is the truth — the server only accepts what it says. The values in Settings were a copy
/// taken when the exe was browsed to, and go stale the moment someone edits the config
/// (Nexus report: port and password changed later, Settings still had the old ones, telnet
/// "connected" but every command was rejected, so the player list stayed at 0).
/// Settings stay as a fallback for a server started with some other config file.
/// </summary>
public static class TelnetCredentials
{
    /// <param name="configFilePath">The server's config file (see <see cref="ServerLaunch.ConfigFilePath(AppSettings)"/>).</param>
    public static IReadOnlyList<TelnetEndpoint> Resolve(
        string? configFilePath, int settingsPort, string? settingsPassword, out bool disabledInConfig)
    {
        disabledInConfig = false;
        var endpoints = new List<TelnetEndpoint>();

        var fromConfig = ReadServerConfig(configFilePath);
        if (fromConfig is { } cfg)
        {
            disabledInConfig = !cfg.Enabled;
            endpoints.Add(new TelnetEndpoint(cfg.Port, cfg.Password, FromServerConfig: true));
        }

        var fromSettings = new TelnetEndpoint(settingsPort, settingsPassword ?? "", FromServerConfig: false);
        if (!endpoints.Any(e => e.Port == fromSettings.Port && e.Password == fromSettings.Password))
            endpoints.Add(fromSettings);

        return endpoints;
    }

    /// <summary>TelnetEnabled/TelnetPort/TelnetPassword from the server's config file, or null if unreadable.</summary>
    public static (bool Enabled, int Port, string Password)? ReadServerConfig(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            var props = XDocument.Load(path).Descendants("property")
                .GroupBy(p => p.Attribute("name")?.Value ?? "")
                .ToDictionary(g => g.Key, g => g.First().Attribute("value")?.Value ?? "");

            var enabled = !props.TryGetValue("TelnetEnabled", out var en) ||
                          !string.Equals(en.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            var port = props.TryGetValue("TelnetPort", out var p) && int.TryParse(p, out var parsed) ? parsed : 8081;
            var password = props.GetValueOrDefault("TelnetPassword") ?? "";
            return (enabled, port, password);
        }
        catch
        {
            return null; // half-written or invalid XML: fall back to Settings
        }
    }
}
