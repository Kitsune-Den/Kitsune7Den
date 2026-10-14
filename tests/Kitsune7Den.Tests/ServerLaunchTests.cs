using System.IO;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.Tests;

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
