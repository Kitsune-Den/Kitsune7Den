using System.IO;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.Tests;

// UseConfigFile saves settings, so this runs alongside the other tests that write the shared test settings file
[Collection("Localization")]
public class ServerLaunchTests
{
    private const string Log = @"C:\Server\output_log_dedi__2026-10-14__10-00-00.txt";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoExtras_IsExactlyTheOriginalCommandLine(string? extras)
    {
        var launch = ServerLaunch.Build(Log, extras);

        Assert.Equal(
            $"-logfile \"{Log}\" -quit -batchmode -nographics -configfile=serverconfig.xml -dedicated",
            launch.Arguments);
        Assert.Empty(launch.Ignored);
    }

    [Fact]
    public void Extras_GoJustBeforeDedicated_LikeTheBatFile()
    {
        var launch = ServerLaunch.Build(Log, "-maxplayers=12 -myflag");

        Assert.EndsWith("-configfile=serverconfig.xml -maxplayers=12 -myflag -dedicated", launch.Arguments);
    }

    [Fact]
    public void ConfigFile_ReplacesTheDefault_NotAddedTwice()
    {
        var launch = ServerLaunch.Build(Log, "-configfile=myconfig.xml");

        Assert.Contains("-configfile=myconfig.xml -dedicated", launch.Arguments);
        Assert.DoesNotContain("serverconfig.xml", launch.Arguments);
    }

    [Fact]
    public void ConfigFile_WithSpaces_StaysOneArgument()
    {
        var launch = ServerLaunch.Build(Log, "-configfile=\"D:\\My Server\\den config.xml\"");

        Assert.Contains("-configfile=\"D:\\My Server\\den config.xml\" -dedicated", launch.Arguments);
    }

    [Fact]
    public void ArgumentsKitsune7DenRelyOn_AreIgnored_AndReported()
    {
        var launch = ServerLaunch.Build(Log, "-logfile other.txt -nographics -dedicated -keepme");

        Assert.Equal(["-logfile other.txt", "-nographics", "-dedicated"], launch.Ignored);
        Assert.DoesNotContain("other.txt", launch.Arguments);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(launch.Arguments, "-dedicated"));
        Assert.EndsWith("-keepme -dedicated", launch.Arguments);
    }

    [Fact]
    public void Tokenize_RespectsQuotes()
    {
        Assert.Equal(["-a", "-b=two words", "plain"], ServerLaunch.Tokenize("  -a   -b=\"two words\" plain "));
    }

    [Theory]
    [InlineData(null, "serverconfig.xml")]
    [InlineData("-foo", "serverconfig.xml")]
    [InlineData("-configfile=den.xml", "den.xml")]
    [InlineData("-CONFIGFILE=den.xml", "den.xml")]
    [InlineData("-configfile=a.xml -configfile=b.xml", "b.xml")]
    [InlineData("-configfile=", "serverconfig.xml")]
    public void ConfigFileName_FromTheArguments(string? extras, string expected)
    {
        Assert.Equal(expected, ServerLaunch.ConfigFileName(extras));
    }

    [Fact]
    public void ConfigFilePath_RelativeIsInTheServerFolder_AbsoluteIsKept()
    {
        Assert.Equal(Path.Combine(@"C:\Server", "den.xml"), ServerLaunch.ConfigFilePath(@"C:\Server", "-configfile=den.xml"));
        Assert.Equal(@"D:\cfg\den.xml", ServerLaunch.ConfigFilePath(@"C:\Server", @"-configfile=D:\cfg\den.xml"));
        Assert.Equal(Path.Combine(@"C:\Server", "serverconfig.xml"), ServerLaunch.ConfigFilePath(@"C:\Server", ""));
    }

    [Theory]
    [InlineData(null, "den.xml", "-configfile=den.xml")]
    [InlineData("-maxplayers=12", "den.xml", "-configfile=den.xml -maxplayers=12")]
    [InlineData("-a -configfile=old.xml -b", "den.xml", "-configfile=den.xml -a -b")]
    [InlineData("-a -CONFIGFILE=old.xml", "serverconfig.xml", "-a")]
    [InlineData("-configfile=old.xml", "SERVERCONFIG.XML", "")]
    [InlineData("-name=\"My Den\"", @"D:\My Server\den.xml", "-configfile=\"D:\\My Server\\den.xml\" -name=\"My Den\"")]
    public void WithConfigFile_ReplacesOnlyTheConfigFile(string? extras, string file, string expected)
    {
        var result = ServerLaunch.WithConfigFile(extras, file);

        Assert.Equal(expected, result);
        Assert.Equal(file, ServerLaunch.ConfigFileName(result), ignoreCase: true);
    }

    [Fact]
    public void ConfigFileArgument_IsTheNameInTheServerFolder_ElseTheFullPath()
    {
        Assert.Equal("den.xml", ServerLaunch.ConfigFileArgument(@"C:\Server", @"C:\Server\den.xml"));
        Assert.Equal("den.xml", ServerLaunch.ConfigFileArgument(@"C:\Server\", @"c:\server\den.xml"));
        Assert.Equal(@"C:\Server\cfg\den.xml", ServerLaunch.ConfigFileArgument(@"C:\Server", @"C:\Server\cfg\den.xml"));
        Assert.Equal(@"D:\den.xml", ServerLaunch.ConfigFileArgument(@"C:\Server", @"D:\den.xml"));
        Assert.Equal(@"D:\den.xml", ServerLaunch.ConfigFileArgument("", @"D:\den.xml"));
    }

    [Fact]
    public void ConfigService_UseConfigFile_SwitchesAndSwitchesBack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Kitsune7DenLaunch_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "den.xml"),
                "<ServerSettings><property name=\"ServerName\" value=\"Custom\" /></ServerSettings>");
            File.WriteAllText(Path.Combine(dir, "notes.xml"), "<notes />");

            var settings = new AppSettings { ServerDirectory = dir, ServerLaunchArguments = "-maxplayers=12" };
            var service = new ConfigService(settings);
            var changed = 0;
            service.ConfigFileChanged += () => changed++;

            Assert.True(ConfigService.LooksLikeServerConfig(Path.Combine(dir, "den.xml")));
            Assert.False(ConfigService.LooksLikeServerConfig(Path.Combine(dir, "notes.xml")));
            Assert.False(ConfigService.LooksLikeServerConfig(Path.Combine(dir, "missing.xml")));

            service.UseConfigFile(Path.Combine(dir, "den.xml"));
            Assert.Equal("-configfile=den.xml -maxplayers=12", settings.ServerLaunchArguments);
            Assert.Equal("Custom", service.LoadConfig().Single(p => p.Name == "ServerName").Value);

            service.UseConfigFile(Path.Combine(dir, "serverconfig.xml"));
            Assert.Equal("-maxplayers=12", settings.ServerLaunchArguments);
            Assert.Equal(2, changed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConfigService_ReadsTheCustomConfigFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Kitsune7DenLaunch_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "serverconfig.xml"),
                "<ServerSettings><property name=\"ServerName\" value=\"Default\" /></ServerSettings>");
            File.WriteAllText(Path.Combine(dir, "den.xml"),
                "<ServerSettings><property name=\"ServerName\" value=\"Custom\" /></ServerSettings>");

            var settings = new AppSettings { ServerDirectory = dir, ServerLaunchArguments = "-configfile=den.xml" };
            var service = new ConfigService(settings);

            Assert.Equal("Custom", service.LoadConfig().Single(p => p.Name == "ServerName").Value);
            Assert.Equal("den.xml", service.ConfigFileName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
