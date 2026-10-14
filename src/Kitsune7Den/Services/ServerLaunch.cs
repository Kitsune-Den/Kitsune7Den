using System.IO;
using System.Text;
using Kitsune7Den.Models;

namespace Kitsune7Den.Services;

/// <summary>The server's command line, and which arguments from the user's extras were dropped.</summary>
public sealed record LaunchCommand(string Arguments, IReadOnlyList<string> Ignored);

/// <summary>
/// Builds the dedicated server's command line the way startdedicated.bat does — the same
/// fixed arguments, with the user's extra arguments (the .bat's %*) just before
/// -dedicated, which the game requires last.
///
/// A -configfile=… in the extras replaces the default serverconfig.xml, and
/// <see cref="ConfigFilePath"/> makes every part of Kitsune7Den read that same file.
/// The arguments Kitsune7Den depends on (-logfile for the Console, -quit/-batchmode/
/// -nographics, -dedicated) can't be overridden; they're dropped and reported.
/// </summary>
public static class ServerLaunch
{
    public const string DefaultConfigFile = "serverconfig.xml";

    // Kitsune7Den always passes these itself
    private static readonly HashSet<string> Managed = new(StringComparer.OrdinalIgnoreCase)
    {
        "-logfile", "-quit", "-batchmode", "-nographics", "-dedicated",
    };

    /// <summary>The config file the server will use: -configfile from the extras, else serverconfig.xml.</summary>
    public static string ConfigFileName(string? extraArguments) =>
        Tokenize(extraArguments)
            .Select(t => SplitOption(t))
            .Where(o => o.Name.Equals("-configfile", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(o.Value))
            .Select(o => o.Value!)
            .LastOrDefault() ?? DefaultConfigFile;

    /// <summary>Full path of the server's config file (relative names are in the server folder, like the game does).</summary>
    public static string ConfigFilePath(AppSettings settings) =>
        ConfigFilePath(settings.ServerDirectory, settings.ServerLaunchArguments);

    public static string ConfigFilePath(string? serverDirectory, string? extraArguments)
    {
        var file = ConfigFileName(extraArguments);
        return Path.IsPathRooted(file) ? file : Path.Combine(serverDirectory ?? "", file);
    }

    public static LaunchCommand Build(string logFile, string? extraArguments)
    {
        var configFile = DefaultConfigFile;
        var kept = new List<string>();
        var ignored = new List<string>();

        var tokens = Tokenize(extraArguments);
        for (var i = 0; i < tokens.Count; i++)
        {
            var (name, value) = SplitOption(tokens[i]);

            if (name.Equals("-configfile", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(value)) configFile = value;
                continue;
            }

            if (Managed.Contains(name))
            {
                ignored.Add(tokens[i]);
                // "-logfile path" (value as its own argument): drop the path too
                if (name.Equals("-logfile", StringComparison.OrdinalIgnoreCase) && value is null &&
                    i + 1 < tokens.Count && !tokens[i + 1].StartsWith('-'))
                {
                    ignored[^1] += " " + tokens[++i];
                }
                continue;
            }

            kept.Add(Quote(tokens[i]));
        }

        var args = new StringBuilder();
        args.Append($"-logfile \"{logFile}\" -quit -batchmode -nographics -configfile={QuoteValue(configFile)}");
        foreach (var k in kept) args.Append(' ').Append(k);
        args.Append(" -dedicated");
        return new LaunchCommand(args.ToString(), ignored);
    }

    /// <summary>
    /// Split like a Windows command line: whitespace separates arguments, double quotes
    /// group (and are removed), so -configfile="C:\My Server\cfg.xml" is one argument.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return tokens;

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        foreach (var c in text)
        {
            if (c == '"') { inQuotes = !inQuotes; hasToken = true; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken) tokens.Add(current.ToString());
                current.Clear();
                hasToken = false;
                continue;
            }
            current.Append(c);
            hasToken = true;
        }
        if (hasToken) tokens.Add(current.ToString());
        return tokens;
    }

    private static (string Name, string? Value) SplitOption(string token)
    {
        var eq = token.IndexOf('=');
        return eq < 0 ? (token, null) : (token[..eq], token[(eq + 1)..]);
    }

    /// <summary>Re-quote an argument for the command line: name="value with spaces", or "whole token".</summary>
    private static string Quote(string token)
    {
        if (!token.Any(char.IsWhiteSpace)) return token;
        var (name, value) = SplitOption(token);
        return value is not null && name.StartsWith('-') ? $"{name}={QuoteValue(value)}" : $"\"{token}\"";
    }

    private static string QuoteValue(string value) =>
        value.Any(char.IsWhiteSpace) || value.Length == 0 ? $"\"{value}\"" : value;
}
