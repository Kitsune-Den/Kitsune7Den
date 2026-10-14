using System.Diagnostics;
using System.IO;
using Kitsune7Den.Localization;
using Kitsune7Den.Models;

namespace Kitsune7Den.Services;

public class ServerProcessService : IDisposable
{
    private Process? _serverProcess;
    private readonly AppSettings _settings;
    private readonly SteamCmdService _steamCmd;
    private DateTime? _startedAt;
    private string? _currentLogFile;

    // Set whenever Kitsune7Den itself asked the server to go away, so the process exiting
    // afterwards isn't mistaken for a crash.
    private volatile bool _stopRequested;

    // Orders the "now Running" transition in StartAsync against the Exited handler, so a
    // process that dies within milliseconds of launch can't be left showing as Running.
    private readonly object _stateLock = new();

    private const string SteamAppId = "251570";
    private const int MaxLogFiles = 20;

    public event Action<ServerState>? StateChanged;
    public event Action<string>? OutputReceived;

    /// <summary>
    /// The server process exited without Kitsune7Den asking it to (crash, killed, or shut
    /// down from somewhere else). Carries the exit code when Windows reports one.
    /// </summary>
    public event Action<int?>? Crashed;

    public ServerState CurrentState { get; private set; } = ServerState.Stopped;

    public ServerProcessService(AppSettings settings, SteamCmdService steamCmd)
    {
        _settings = settings;
        _steamCmd = steamCmd;

        // Pipe SteamCMD output through to console during auto-update
        _steamCmd.OutputReceived += line => OutputReceived?.Invoke($"[SteamCMD] {line}");
    }

    public TimeSpan Uptime => _startedAt.HasValue ? DateTime.UtcNow - _startedAt.Value : TimeSpan.Zero;

    /// <summary>Local time the running server was started, or null when stopped.</summary>
    public DateTime? StartedAt => _startedAt?.ToLocalTime();

    /// <summary>
    /// Mark a shutdown that's on its way through another path (e.g. "shutdown" typed into
    /// the console), so the exit isn't treated as a crash.
    /// </summary>
    public void NoteShutdownRequested() => _stopRequested = true;

    public async Task<bool> StartAsync()
    {
        if (CurrentState != ServerState.Stopped)
            return false;

        if (string.IsNullOrWhiteSpace(_settings.ServerExePath) || !File.Exists(_settings.ServerExePath))
            return false;

        var serverDir = Path.GetDirectoryName(_settings.ServerExePath) ?? "";

        _stopRequested = false;
        SetState(ServerState.Starting);

        var configPath = Path.Combine(serverDir, "serverconfig.xml");
        var configBackup = configPath + ".bak";

        // ── Step 1: Back up config BEFORE SteamCMD can touch it ──
        if (File.Exists(configPath))
        {
            try
            {
                File.Copy(configPath, configBackup, overwrite: true);
                OutputReceived?.Invoke(Loc.T("Server_ConfigBackedUp"));
            }
            catch (Exception ex)
            {
                OutputReceived?.Invoke("[WARN] " + Loc.F("Server_ConfigBackupFailed", ex.Message));
            }
        }

        // ── Step 2: Auto-update via SteamCMD (if enabled) ──
        if (_settings.AutoUpdateOnStart && _steamCmd.IsSteamCmdAvailable)
        {
            OutputReceived?.Invoke(Loc.T("Server_CheckingUpdates"));
            var updated = await _steamCmd.InstallOrUpdateServerAsync();
            OutputReceived?.Invoke(Loc.T(updated ? "Server_UpdateCheckComplete" : "Server_UpdateCheckFailed"));
        }

        // ── Step 3: Restore config from backup (undo any Steam overwrites) ──
        if (File.Exists(configBackup))
        {
            try
            {
                File.Copy(configBackup, configPath, overwrite: true);
                OutputReceived?.Invoke(Loc.T("Server_ConfigRestored"));
            }
            catch (Exception ex)
            {
                OutputReceived?.Invoke("[WARN] " + Loc.F("Server_ConfigRestoreFailed", ex.Message));
            }
        }

        // ── Step 3: Rotate old logs (keep latest 20) ──
        RotateLogs(serverDir);

        // ── Step 4: Write steam_appid.txt + set env vars ──
        try
        {
            await File.WriteAllTextAsync(Path.Combine(serverDir, "steam_appid.txt"), SteamAppId);
        }
        catch { /* non-critical */ }

        // ── Step 5: Build log filename with timestamp ──
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd__HH-mm-ss");
        var logFileName = $"output_log_dedi__{timestamp}.txt";
        _currentLogFile = Path.Combine(serverDir, logFileName);

        OutputReceived?.Invoke(Loc.F("Server_LogFile", logFileName));

        // ── Step 6: Launch server with proper arguments ──
        var args = $"-logfile \"{_currentLogFile}\" -quit -batchmode -nographics -configfile=serverconfig.xml -dedicated";

        var startInfo = new ProcessStartInfo
        {
            FileName = _settings.ServerExePath,
            Arguments = args,
            WorkingDirectory = serverDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Set Steam environment variables
        startInfo.EnvironmentVariables["SteamAppId"] = SteamAppId;
        startInfo.EnvironmentVariables["SteamGameId"] = SteamAppId;

        _serverProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _serverProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                OutputReceived?.Invoke(e.Data);
        };
        _serverProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                OutputReceived?.Invoke($"[ERR] {e.Data}");
        };
        var process = _serverProcess;
        process.Exited += (_, _) =>
        {
            bool unexpected;
            int? exitCode = null;
            lock (_stateLock)
            {
                // A stale process from before a restart must not touch the new one's state
                if (!ReferenceEquals(process, _serverProcess)) return;

                unexpected = !_stopRequested && CurrentState is ServerState.Running or ServerState.Starting;
                try { exitCode = process.ExitCode; } catch { /* not available */ }

                _startedAt = null;
                SetState(ServerState.Stopped);
            }

            if (unexpected)
            {
                OutputReceived?.Invoke("[WARN] " + Loc.F("Server_ExitedUnexpectedly", exitCode?.ToString() ?? "?"));
                Crashed?.Invoke(exitCode);
            }
        };

        try
        {
            OutputReceived?.Invoke(Loc.F("Server_StartingCommand", Path.GetFileName(_settings.ServerExePath), args));
            _serverProcess.Start();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();
            lock (_stateLock)
            {
                // If it already died, the Exited handler has reported it; don't resurrect the state
                if (process.HasExited) return false;
                _startedAt = DateTime.UtcNow;
                SetState(ServerState.Running);
            }
            return true;
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke("[ERR] " + Loc.F("Server_StartFailed", ex.Message));
            SetState(ServerState.Stopped);
            return false;
        }
    }

    public async Task StopAsync(TelnetService? telnet = null, int gracefulTimeoutMs = 15000)
    {
        if (CurrentState != ServerState.Running)
            return;

        _stopRequested = true;
        SetState(ServerState.Stopping);

        if (telnet is { IsConnected: true })
        {
            OutputReceived?.Invoke(Loc.T("Server_SendingShutdown"));
            await telnet.SendCommandAsync("shutdown");
            var sw = Stopwatch.StartNew();
            while (_serverProcess is { HasExited: false } && sw.ElapsedMilliseconds < gracefulTimeoutMs)
                await Task.Delay(500);
        }

        if (_serverProcess is { HasExited: false })
        {
            OutputReceived?.Invoke(Loc.T("Server_ForceKilling"));
            try { _serverProcess.Kill(entireProcessTree: true); }
            catch { /* already exited */ }
        }

        _startedAt = null;
        SetState(ServerState.Stopped);
        OutputReceived?.Invoke(Loc.T("Server_Stopped"));
    }

    public async Task RestartAsync(TelnetService? telnet = null, int gracefulTimeoutMs = 15000)
    {
        await StopAsync(telnet, gracefulTimeoutMs);
        await Task.Delay(2000);
        await StartAsync();
    }

    public bool IsProcessRunning()
    {
        return _serverProcess is { HasExited: false };
    }

    public string? CurrentLogFile => _currentLogFile;

    private void RotateLogs(string serverDir)
    {
        try
        {
            var logFiles = Directory.GetFiles(serverDir, "output_log_dedi__*.txt")
                .OrderByDescending(f => f)
                .ToArray();

            if (logFiles.Length > MaxLogFiles)
            {
                var toDelete = logFiles.Skip(MaxLogFiles);
                foreach (var file in toDelete)
                {
                    try
                    {
                        File.Delete(file);
                        OutputReceived?.Invoke(Loc.F("Server_CleanedOldLog", Path.GetFileName(file)));
                    }
                    catch { /* file may be locked */ }
                }
            }
        }
        catch { /* non-critical */ }
    }

    private void SetState(ServerState state)
    {
        CurrentState = state;
        StateChanged?.Invoke(state);
    }

    public void Dispose()
    {
        _stopRequested = true;
        if (_serverProcess is { HasExited: false })
        {
            try { _serverProcess.Kill(entireProcessTree: true); }
            catch { /* best effort */ }
        }
        _serverProcess?.Dispose();
    }
}
