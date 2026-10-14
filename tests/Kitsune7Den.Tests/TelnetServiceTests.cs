using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.Tests;

/// <summary>
/// Telnet login against a fake 7D2D console that speaks the real prompts. Covers the Nexus
/// report where telnet showed "Connected" with a stale password, every command was
/// rejected, and the player list sat at 0.
/// </summary>
[Collection("Localization")]
public class TelnetServiceTests : IDisposable
{
    private readonly string _serverDir = Path.Combine(Path.GetTempPath(), "Kitsune7DenTelnet_" + Guid.NewGuid().ToString("N")[..8]);

    public TelnetServiceTests() => Directory.CreateDirectory(_serverDir);

    public void Dispose()
    {
        try { Directory.Delete(_serverDir, recursive: true); }
        catch { /* best effort */ }
    }

    private void WriteServerConfig(int port, string password, bool enabled = true) =>
        File.WriteAllText(Path.Combine(_serverDir, "serverconfig.xml"), $"""
            <?xml version="1.0"?>
            <ServerSettings>
              <property name="TelnetEnabled" value="{(enabled ? "true" : "false")}" />
              <property name="TelnetPort" value="{port}" />
              <property name="TelnetPassword" value="{password}" />
            </ServerSettings>
            """);

    private AppSettings Settings(int port, string password) => new()
    {
        ServerDirectory = _serverDir,
        TelnetPort = port,
        TelnetPassword = password,
    };

    [Fact]
    public async Task StaleSettings_UseServerConfig_AndSettingsCatchUp()
    {
        // The reported case: port and password changed in serverconfig.xml after Browse
        using var server = FakeSevenDaysConsole.Start(password: "fox-den");
        WriteServerConfig(server.Port, "fox-den");
        var settings = Settings(port: 8081, password: "old-password");
        using var telnet = new TelnetService(settings);

        var result = await telnet.ConnectAsync();

        Assert.Equal(TelnetConnectResult.Connected, result);
        Assert.True(telnet.IsConnected);
        Assert.Equal(server.Port, settings.TelnetPort);
        Assert.Equal("fox-den", settings.TelnetPassword);
    }

    [Fact]
    public async Task WrongPassword_IsNotConnected_AndSaysSo()
    {
        using var server = FakeSevenDaysConsole.Start(password: "fox-den");
        WriteServerConfig(server.Port, "not-it");
        using var telnet = new TelnetService(Settings(server.Port, "not-it"));
        var messages = new List<string>();
        var states = new List<bool>();
        telnet.DataReceived += messages.Add;
        telnet.ConnectionStateChanged += states.Add;

        var result = await telnet.ConnectAsync();

        Assert.Equal(TelnetConnectResult.WrongPassword, result);
        Assert.False(telnet.IsConnected);
        Assert.Equal(TelnetConnectResult.WrongPassword, telnet.LastResult);
        Assert.False(states.Last());
        Assert.Contains(messages, m => m.StartsWith("[Telnet]") && m.Contains("password"));
        // Commands are refused rather than sent into a rejected session
        Assert.Equal("", await telnet.SendCommandAsync("listplayers"));
    }

    [Fact]
    public async Task ServerWantsAPassword_WeHaveNone()
    {
        using var server = FakeSevenDaysConsole.Start(password: "fox-den");
        WriteServerConfig(server.Port, "");
        using var telnet = new TelnetService(Settings(server.Port, ""));

        Assert.Equal(TelnetConnectResult.PasswordRequired, await telnet.ConnectAsync());
        Assert.False(telnet.IsConnected);
    }

    [Fact]
    public async Task NoPasswordServer_ConnectsWithoutAPrompt()
    {
        using var server = FakeSevenDaysConsole.Start(password: null);
        WriteServerConfig(server.Port, "");
        using var telnet = new TelnetService(Settings(server.Port, ""));

        Assert.Equal(TelnetConnectResult.Connected, await telnet.ConnectAsync());
        Assert.True(telnet.IsConnected);
    }

    [Fact]
    public async Task TelnetDisabledInConfig_DoesNotTry()
    {
        using var server = FakeSevenDaysConsole.Start(password: null);
        WriteServerConfig(server.Port, "", enabled: false);
        using var telnet = new TelnetService(Settings(server.Port, ""));

        Assert.Equal(TelnetConnectResult.TelnetDisabled, await telnet.ConnectAsync());
        Assert.Equal(0, server.Connections);
    }

    [Fact]
    public async Task NothingListening_IsUnreachable()
    {
        var port = FreePort();
        WriteServerConfig(port, "");
        using var telnet = new TelnetService(Settings(port, ""));

        Assert.Equal(TelnetConnectResult.Unreachable, await telnet.ConnectAsync());
    }

    [Fact]
    public async Task NoServerConfig_FallsBackToSettings()
    {
        using var server = FakeSevenDaysConsole.Start(password: "fox-den");
        using var telnet = new TelnetService(Settings(server.Port, "fox-den"));

        Assert.Equal(TelnetConnectResult.Connected, await telnet.ConnectAsync());
    }

    [Fact]
    public async Task Players_AreRead_AfterLogin_EvenOnACommaDecimalWindows()
    {
        using var server = FakeSevenDaysConsole.Start(password: "fox-den");
        WriteServerConfig(server.Port, "fox-den");
        using var telnet = new TelnetService(Settings(server.Port, "fox-den"));
        await telnet.ConnectAsync();

        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var players = await telnet.GetPlayersAsync();

            var p = Assert.Single(players);
            Assert.Equal("NonToxThicc", p.Name);
            Assert.Equal(-303.4f, p.X, 0.01f);
            Assert.Equal(-1201.6f, p.Z, 0.01f);
            Assert.Equal(3, p.Level);
            Assert.Equal("Steam_76561198000000000", p.PlatformId);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Credentials_ServerConfigFirst_SettingsAsFallback()
    {
        WriteServerConfig(8084, "new");
        var endpoints = TelnetCredentials.Resolve(Path.Combine(_serverDir, "serverconfig.xml"), 8081, "old", out var disabled);

        Assert.False(disabled);
        Assert.Equal([new TelnetEndpoint(8084, "new", true), new TelnetEndpoint(8081, "old", false)], endpoints);
    }

    [Fact]
    public void Credentials_SameValues_AreTriedOnce()
    {
        WriteServerConfig(8081, "same");
        Assert.Single(TelnetCredentials.Resolve(Path.Combine(_serverDir, "serverconfig.xml"), 8081, "same", out _));
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>Just enough of the 7D2D telnet console: login prompts and listplayers.</summary>
    private sealed class FakeSevenDaysConsole : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly string? _password;
        private readonly CancellationTokenSource _cts = new();
        private int _connections;

        public int Port { get; }
        public int Connections => _connections;

        private FakeSevenDaysConsole(string? password)
        {
            _password = password;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync();
        }

        public static FakeSevenDaysConsole Start(string? password) => new(password);

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    Interlocked.Increment(ref _connections);
                    _ = HandleAsync(client);
                }
            }
            catch { /* stopped */ }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using var _ = client;
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8);
            async Task Send(string text) => await stream.WriteAsync(Encoding.UTF8.GetBytes(text));

            try
            {
                if (_password is not null)
                {
                    await Send("Please enter password:");
                    while (true)
                    {
                        var attempt = await reader.ReadLineAsync();
                        if (attempt is null) return;
                        if (attempt.Trim() == _password) break;
                        await Send("Password incorrect, please enter password:");
                    }
                    await Send("Logon successful.\r\n\r\n");
                }
                await Send("*** Connected with 7DTD server.\r\n*** Server version: V 3.3 (b18)\r\n\r\n");

                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.Trim() != "listplayers") continue;
                    await Send(
                        "2026-10-14T10:00:00 100.000 INF Executing command 'listplayers' by Telnet from 127.0.0.1:50000\r\n" +
                        "0. id=171, NonToxThicc, pos=(-303.4, 39.1, -1201.6), rot=(-11.3, 98.4, 0.0), remote=True, " +
                        "health=96, deaths=0, zombies=38, players=0, score=37, level=3, pltfmid=Steam_76561198000000000, " +
                        "crossid=EOS_0002abcdef, ip=192.168.1.5, ping=12\r\n" +
                        "Total of 1 in the game\r\n");
                }
            }
            catch { /* client went away */ }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }
}
