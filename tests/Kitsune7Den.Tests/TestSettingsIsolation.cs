using System.IO;
using System.Runtime.CompilerServices;
using Kitsune7Den.Models;

namespace Kitsune7Den.Tests;

/// <summary>
/// Points AppSettings at a throwaway file before any test runs. Code under test calls
/// AppSettings.Save() (e.g. telnet remembering a working password), and without this it
/// would overwrite the real %LocalAppData%\Kitsune7Den\settings.json on the dev's machine.
/// </summary>
internal static class TestSettingsIsolation
{
    public static readonly string SettingsFile = Path.Combine(
        Path.GetTempPath(), "Kitsune7DenTests_settings_" + Guid.NewGuid().ToString("N")[..8], "settings.json");

    [ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable(AppSettings.PathOverrideVariable, SettingsFile);
}

public class TestSettingsIsolationTests
{
    [Fact]
    public void Save_WritesToTheTestFile_NotLocalAppData()
    {
        var real = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kitsune7Den", "settings.json");
        var realBefore = File.Exists(real) ? File.GetLastWriteTimeUtc(real) : (DateTime?)null;

        new AppSettings { TelnetPassword = "isolation-check" }.Save();

        Assert.Contains("isolation-check", File.ReadAllText(TestSettingsIsolation.SettingsFile));
        Assert.Equal(realBefore, File.Exists(real) ? File.GetLastWriteTimeUtc(real) : null);
    }
}
