using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Kitsune7Den.Localization;
using Kitsune7Den.Models;

namespace Kitsune7Den.Services;

public enum TelnetConnectResult
{
    Connected,
    /// <summary>Nothing listening yet (server still booting, or wrong port).</summary>
    Unreachable,
    /// <summary>The server asked for a password and rejected the one we have.</summary>
    WrongPassword,
    /// <summary>The server asked for a password and we don't have one.</summary>
    PasswordRequired,
    /// <summary>serverconfig.xml has TelnetEnabled = false.</summary>
    TelnetDisabled,
}

public partial class TelnetService : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    // The server greets with its password prompt straight away; no prompt means no password.
    private static readonly TimeSpan PromptTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(5);

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readCts;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    // Login handshake: the read loop reports what it sees until the login is settled
    private volatile bool _authenticated;
    private Channel<AuthEvent>? _authEvents;
    private string _authBuffer = "";

    private enum AuthEvent { PasswordPrompt, LogonSuccessful, PasswordIncorrect }

    public event Action<string>? DataReceived;
    public event Action<bool>? ConnectionStateChanged;

    /// <summary>Connected AND logged in. A socket the server hasn't accepted a password on doesn't count.</summary>
    public bool IsConnected => _authenticated && _client?.Connected == true;

    /// <summary>The outcome of the last connection attempt.</summary>
    public TelnetConnectResult LastResult { get; private set; } = TelnetConnectResult.Unreachable;

    public TelnetService(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Connect and log in. Port and password come from the server's serverconfig.xml when
    /// it exists (that's what the server accepts), with Settings as a fallback. Only a
    /// confirmed login counts as connected; anything else is explained in the Console.
    /// </summary>
    /// <param name="reportUnreachable">
    /// Also explain "nothing is listening" in the Console. Off for background retries while
    /// a server boots, where that's expected and would just be noise.
    /// </param>
    public async Task<TelnetConnectResult> ConnectAsync(bool reportUnreachable = false)
    {
        await _connectLock.WaitAsync();
        try
        {
            var endpoints = TelnetCredentials.Resolve(
                _settings.ServerDirectory, _settings.TelnetPort, _settings.TelnetPassword, out var disabled);

            var result = TelnetConnectResult.Unreachable;
            if (disabled)
            {
                result = TelnetConnectResult.TelnetDisabled;
            }
            else
            {
                foreach (var endpoint in endpoints)
                {
                    var attempt = await TryConnectAsync(endpoint);
                    if (attempt == TelnetConnectResult.Connected)
                    {
                        RememberWorkingEndpoint(endpoint);
                        result = attempt;
                        break;
                    }
                    // Keep the most telling failure: "wrong password" beats "nothing on that port"
                    if (result == TelnetConnectResult.Unreachable)
                        result = attempt;
                }
            }

            LastResult = result;
            if (result != TelnetConnectResult.Connected)
            {
                CloseSocket();
                if (result != TelnetConnectResult.Unreachable || reportUnreachable)
                    DataReceived?.Invoke("[Telnet] " + Loc.F($"Telnet_Result_{result}", endpoints.FirstOrDefault()?.Port ?? _settings.TelnetPort));
            }
            ConnectionStateChanged?.Invoke(result == TelnetConnectResult.Connected);
            return result;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task<TelnetConnectResult> TryConnectAsync(TelnetEndpoint endpoint)
    {
        CloseSocket();

        var client = new TcpClient();
        try
        {
            using var timeout = new CancellationTokenSource(ConnectTimeout);
            await client.ConnectAsync("127.0.0.1", endpoint.Port, timeout.Token);
        }
        catch
        {
            client.Dispose();
            return TelnetConnectResult.Unreachable;
        }

        _client = client;
        _stream = client.GetStream();
        _authenticated = false;
        _authBuffer = "";
        _authEvents = Channel.CreateUnbounded<AuthEvent>();
        _readCts = new CancellationTokenSource();
        _ = ReadLoopAsync(_stream, _readCts.Token);

        var first = await NextAuthEventAsync(PromptTimeout);
        if (first is null or AuthEvent.LogonSuccessful)
        {
            // No password prompt: a server with a blank TelnetPassword (localhost only)
            return Authenticated();
        }

        if (string.IsNullOrEmpty(endpoint.Password))
            return TelnetConnectResult.PasswordRequired;

        await WriteRawAsync(endpoint.Password);
        return await NextAuthEventAsync(LoginTimeout) == AuthEvent.LogonSuccessful
            ? Authenticated()
            : TelnetConnectResult.WrongPassword;

        TelnetConnectResult Authenticated()
        {
            _authenticated = true;
            _authEvents = null;
            return _client?.Connected == true ? TelnetConnectResult.Connected : TelnetConnectResult.Unreachable;
        }
    }

    private async Task<AuthEvent?> NextAuthEventAsync(TimeSpan timeout)
    {
        if (_authEvents is not { } events) return null;
        using var cts = new CancellationTokenSource(timeout);
        try { return await events.Reader.ReadAsync(cts.Token); }
        catch (OperationCanceledException) { return null; }
        catch (ChannelClosedException) { return null; }
    }

    /// <summary>
    /// Watch raw incoming text for the login prompts. The password prompt has no newline
    /// after it, so this looks at partial data, not just complete lines.
    /// </summary>
    private void InspectForLogin(string chunk)
    {
        if (_authenticated || _authEvents is not { } events) return;

        _authBuffer += chunk;
        // Order matters: "Password incorrect, please enter password:" contains the prompt too
        // "*** Connected with 7DTD server." is the greeting once logged in, and straight away
        // on a server without a password, so it counts as success without waiting for a prompt
        if (_authBuffer.Contains("Logon successful", StringComparison.OrdinalIgnoreCase) ||
            _authBuffer.Contains("Connected with 7DTD", StringComparison.OrdinalIgnoreCase))
            events.Writer.TryWrite(AuthEvent.LogonSuccessful);
        else if (_authBuffer.Contains("incorrect", StringComparison.OrdinalIgnoreCase))
            events.Writer.TryWrite(AuthEvent.PasswordIncorrect);
        else if (_authBuffer.Contains("enter password", StringComparison.OrdinalIgnoreCase))
            events.Writer.TryWrite(AuthEvent.PasswordPrompt);
        else
            return;

        _authBuffer = "";
    }

    /// <summary>Keep Settings in step with what actually worked, so it shows the real values.</summary>
    private void RememberWorkingEndpoint(TelnetEndpoint endpoint)
    {
        if (_settings.TelnetPort == endpoint.Port && _settings.TelnetPassword == endpoint.Password) return;
        _settings.TelnetPort = endpoint.Port;
        _settings.TelnetPassword = endpoint.Password;
        try { _settings.Save(); } catch { /* best effort */ }
    }

    private async Task WriteRawAsync(string text)
    {
        if (_stream is null) return;
        var bytes = Encoding.UTF8.GetBytes(text + "\n");
        await _stream.WriteAsync(bytes);
    }

    private void CloseSocket()
    {
        _authenticated = false;
        _authEvents?.Writer.TryComplete();
        _authEvents = null;

        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = null;

        _stream?.Dispose();
        _stream = null;

        _client?.Dispose();
        _client = null;
    }

    public void Disconnect()
    {
        CloseSocket();
        ConnectionStateChanged?.Invoke(false);
    }

    public async Task<string> SendCommandAsync(string command)
    {
        if (_stream is null || !IsConnected)
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(command + "\n");
        await _stream.WriteAsync(bytes);
        await Task.Delay(200); // brief wait for response
        return command;
    }

    public async Task<List<PlayerInfo>> GetPlayersAsync()
    {
        if (!IsConnected) return [];

        _lastPlayerList = [];
        await SendCommandAsync("listplayers");
        await Task.Delay(2000); // wait for full response

        return _lastPlayerList;
    }

    private List<PlayerInfo> _lastPlayerList = [];

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();
        var decoder = Encoding.UTF8.GetDecoder(); // keeps multi-byte characters split across reads intact

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var bytesRead = await stream.ReadAsync(buffer, ct);
                if (bytesRead == 0) break;

                var chars = new char[decoder.GetCharCount(buffer, 0, bytesRead)];
                decoder.GetChars(buffer, 0, bytesRead, chars, 0);
                var text = new string(chars);
                InspectForLogin(text);
                sb.Append(text);

                // Process complete lines
                var content = sb.ToString();
                var lastNewline = content.LastIndexOf('\n');
                if (lastNewline >= 0)
                {
                    var complete = content[..(lastNewline + 1)];
                    sb.Clear();
                    sb.Append(content[(lastNewline + 1)..]);

                    foreach (var line in complete.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = line.TrimEnd('\r');
                        ParsePlayerLine(trimmed);
                        DataReceived?.Invoke(trimmed);
                    }
                }
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception) { }

        // The server closed the connection (shutdown, restart, or too many bad logins)
        if (!ct.IsCancellationRequested && ReferenceEquals(stream, _stream))
        {
            _authenticated = false;
            _authEvents?.Writer.TryComplete();
            ConnectionStateChanged?.Invoke(false);
        }
    }

    private void ParsePlayerLine(string line)
    {
        // 7D2D listplayers format (may be prefixed with timestamp from telnet):
        // 0. id=171, NonToxThicc, pos=(-303.4, 39.1, -1201.6), rot=(...), remote=True, health=96, deaths=0, zombies=38, players=0, score=37, level=3, ...
        // Total of 1 in the game

        // Strip timestamp prefix if present (e.g. "2026-04-06T10:30:04 176.012 INF ")
        var stripped = TimestampPrefixRegex().Replace(line, "");

        if (stripped.Contains("Total of ") && stripped.Contains(" in the game"))
        {
            // Don't clear — we build up the list as lines arrive
            return;
        }

        // Also detect "Executing command 'listplayers'" to reset the list
        if (stripped.Contains("Executing command 'listplayers'"))
        {
            _lastPlayerList = [];
            return;
        }

        var match = PlayerLineRegex().Match(stripped);
        if (match.Success)
        {
            var player = new PlayerInfo
            {
                EntityId = int.TryParse(match.Groups["id"].Value, out var id) ? id : 0,
                Name = match.Groups["name"].Value,
                // The server always writes "-303.4"; a German or French Windows would read that as 0
                X = float.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0,
                Y = float.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ? y : 0,
                Z = float.TryParse(match.Groups["z"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ? z : 0
            };

            // Parse additional fields from the rest of the line
            var rest = match.Groups["rest"].Value;
            if (ExtractField(rest, "health") is { } h && int.TryParse(h, out var health)) player.Health = health;
            if (ExtractField(rest, "deaths") is { } d && int.TryParse(d, out var deaths)) player.Deaths = deaths;
            if (ExtractField(rest, "zombies") is { } z2 && int.TryParse(z2, out var zk)) player.ZombieKills = zk;
            if (ExtractField(rest, "players") is { } pk && int.TryParse(pk, out var pkills)) player.PlayerKills = pkills;
            if (ExtractField(rest, "score") is { } sc && int.TryParse(sc, out var score)) player.Score = score;
            if (ExtractField(rest, "level") is { } lv && int.TryParse(lv, out var level)) player.Level = level;
            if (ExtractField(rest, "ip") is { } ip) player.Ip = ip;
            if (ExtractField(rest, "ping") is { } pg && int.TryParse(pg, out var ping)) player.Ping = ping;
            if (ExtractField(rest, "pltfmid") is { } pid) player.PlatformId = pid;
            if (ExtractField(rest, "crossid") is { } cid) player.CrossId = cid;

            _lastPlayerList.Add(player);
        }
    }

    private static string? ExtractField(string text, string fieldName)
    {
        var pattern = fieldName + "=";
        var idx = text.IndexOf(pattern, StringComparison.Ordinal);
        if (idx < 0) return null;
        var start = idx + pattern.Length;
        var end = text.IndexOf(',', start);
        return end < 0 ? text[start..].Trim() : text[start..end].Trim();
    }

    // Matches: 0. id=171, NonToxThicc, pos=(-303.4, 39.1, -1201.6), <rest...>
    [GeneratedRegex(@"^(\d+)\.\s+id=(?<id>\d+),\s+(?<name>[^,]+),\s+pos=\((?<x>[\d.-]+),\s*(?<y>[\d.-]+),\s*(?<z>[\d.-]+)\)(?<rest>.*)")]
    private static partial Regex PlayerLineRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T[\d:]+\s+[\d.]+\s+\w+\s+")]
    private static partial Regex TimestampPrefixRegex();

    public void Dispose()
    {
        CloseSocket();
        _connectLock.Dispose();
    }
}
