using Kitsune7Den.Localization;
using Kitsune7Den.Models;

namespace Kitsune7Den.Services;

/// <summary>
/// Automatic restarts (GitHub issue #4):
/// <list type="bullet">
/// <item><b>Crash restart</b> — when the server exits without Kitsune7Den asking it to, start
/// it again after a short delay, at most N times in a window so a broken server can't loop.</item>
/// <item><b>Scheduled restart</b> — daily at set times, or after N hours of uptime. Players get
/// a countdown of in-game <c>say</c> warnings, then the world is saved and the server is
/// shut down gracefully and started again.</item>
/// </list>
/// Timing is decided by <see cref="RestartSchedule"/>; this class owns the timer and the I/O.
/// </summary>
public sealed class RestartService : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CrashRestartDelay = TimeSpan.FromSeconds(15);
    private const int ScheduledShutdownTimeoutMs = 90_000; // 3.x can take a while to save on shutdown

    private readonly AppSettings _settings;
    private readonly ServerProcessService _process;
    private readonly TelnetService _telnet;
    private readonly Timer _timer;
    private readonly object _gate = new();

    private CrashRestartPolicy _crashPolicy;
    private CancellationTokenSource? _countdown;
    private DateTime _countdownTarget;
    private bool _busy;

    // A cancelled occurrence: scheduling resumes after it instead of picking it again.
    private DateTime? _skippedRestart;

    /// <summary>Status lines for the console (already translated).</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Raised when <see cref="NextRestart"/> or <see cref="CountdownActive"/> changes.</summary>
    public event Action? StateChanged;

    /// <summary>When the next scheduled restart will happen, or null if none is planned.</summary>
    public DateTime? NextRestart { get; private set; }

    /// <summary>True while warnings are being sent for an imminent restart.</summary>
    public bool CountdownActive => _countdown is not null;

    public RestartService(AppSettings settings, ServerProcessService process, TelnetService telnet)
    {
        _settings = settings;
        _process = process;
        _telnet = telnet;
        _crashPolicy = NewCrashPolicy();

        _process.Crashed += OnCrashed;
        _process.StateChanged += state =>
        {
            // A fresh start resets the interval clock, so a skip no longer applies
            if (state == ServerState.Running) lock (_gate) _skippedRestart = null;
            Reschedule();
        };

        _timer = new Timer(_ => Tick(), null, TickInterval, TickInterval);
        Reschedule();
    }

    /// <summary>Call after the restart settings change.</summary>
    public void ApplySettings()
    {
        _crashPolicy = NewCrashPolicy();
        Reschedule();
    }

    private CrashRestartPolicy NewCrashPolicy() =>
        new(_settings.CrashRestartMaxAttempts, TimeSpan.FromMinutes(Math.Max(1, _settings.CrashRestartWindowMinutes)));

    // ── Scheduled restarts ──

    private IReadOnlyList<int> Warnings => RestartSchedule.ParseWarnings(_settings.RestartWarningMinutes) ?? [];

    /// <summary>Work out the next restart from the settings and the server's state.</summary>
    private void Reschedule()
    {
        lock (_gate)
        {
            if (CountdownActive || _busy) return;

            DateTime? next = null;
            if (_settings.ScheduledRestartEnabled && _process.CurrentState == ServerState.Running)
            {
                var now = DateTime.Now;
                var skipped = _skippedRestart is { } s && s > now.AddMinutes(-1) ? s : (DateTime?)null;
                if (_settings.ScheduledRestartMode == nameof(RestartScheduleMode.Interval))
                {
                    if (_settings.ScheduledRestartIntervalHours > 0 && _process.StartedAt is { } started)
                    {
                        // After a skip, count the interval from the skipped restart
                        var from = skipped is { } k && k > started ? k : started;
                        next = RestartSchedule.NextInterval(from, _settings.ScheduledRestartIntervalHours, now,
                            Warnings.DefaultIfEmpty(1).Min());
                    }
                }
                else if (RestartSchedule.ParseTimes(_settings.ScheduledRestartTimes) is { } times)
                {
                    next = RestartSchedule.NextDaily(skipped is { } k && k > now ? k : now, times);
                }
            }

            if (next == NextRestart) return;
            NextRestart = next;
        }
        StateChanged?.Invoke();
    }

    private void Tick()
    {
        DateTime restartAt;
        lock (_gate)
        {
            if (NextRestart is not { } next || CountdownActive || _busy) return;
            var lead = TimeSpan.FromMinutes(Warnings.DefaultIfEmpty(0).Max());
            if (DateTime.Now < next - lead) return;
            restartAt = next;
        }
        _ = RunCountdownAsync(restartAt, scheduled: true);
    }

    /// <summary>Start the warning countdown now, restarting after the longest warning.</summary>
    public void RestartWithWarnings()
    {
        var lead = Warnings.DefaultIfEmpty(0).Max();
        _ = RunCountdownAsync(DateTime.Now.AddMinutes(lead), scheduled: false);
    }

    /// <summary>Cancel a running countdown; the server keeps running.</summary>
    public void CancelCountdown() => CancelCountdown(announce: true);

    private void CancelCountdown(bool announce)
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _countdown;
            if (cts is null) return;
            _countdown = null;
            _skippedRestart = _countdownTarget;
            NextRestart = null;
        }

        cts.Cancel();
        if (announce)
        {
            _ = SayAsync(Loc.T("Restart_CancelledInGame"));
            StatusChanged?.Invoke(Loc.T("Restart_Cancelled"));
        }
        StateChanged?.Invoke();
        Reschedule();
    }

    private async Task RunCountdownAsync(DateTime restartAt, bool scheduled)
    {
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            if (CountdownActive || _busy || _process.CurrentState != ServerState.Running)
            {
                cts.Dispose();
                return;
            }
            _countdown = cts;
            _countdownTarget = restartAt;
            NextRestart = restartAt;
        }
        StateChanged?.Invoke();

        var token = cts.Token;
        try
        {
            StatusChanged?.Invoke(Loc.F(scheduled ? "Restart_ScheduledStarting" : "Restart_ManualStarting",
                restartAt.ToString("HH:mm", Loc.Instance.Culture)));

            await EnsureTelnetAsync();

            foreach (var minutes in RestartSchedule.WarningsToSend(Warnings, restartAt - DateTime.Now))
            {
                await DelayUntilAsync(restartAt.AddMinutes(-minutes), token);
                await SayAsync(RestartSchedule.FormatWarning(_settings.RestartWarningMessage, minutes,
                    Loc.T("Restart_DefaultWarningMessage")));
            }

            await DelayUntilAsync(restartAt, token);

            lock (_gate)
            {
                if (!ReferenceEquals(_countdown, cts)) return; // cancelled at the last moment
                _countdown = null;
                _busy = true;
            }
            StateChanged?.Invoke();

            if (_process.CurrentState != ServerState.Running)
            {
                StatusChanged?.Invoke(Loc.T("Restart_NotRunning"));
                return;
            }

            await RestartNowAsync();
        }
        catch (OperationCanceledException)
        {
            // CancelCountdown already reported it
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("[WARN] " + Loc.F("Restart_Failed", ex.Message));
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_countdown, cts)) _countdown = null;
                _busy = false;
                NextRestart = null;
            }
            cts.Dispose();
            StateChanged?.Invoke();
            Reschedule();
        }
    }

    /// <summary>Final message, save, graceful stop, start, reconnect telnet.</summary>
    private async Task RestartNowAsync()
    {
        var finalMessage = string.IsNullOrWhiteSpace(_settings.RestartFinalMessage)
            ? Loc.T("Restart_DefaultFinalMessage")
            : _settings.RestartFinalMessage;
        await SayAsync(finalMessage);

        if (_telnet.IsConnected)
        {
            StatusChanged?.Invoke(Loc.T("Restart_Saving"));
            await _telnet.SendCommandAsync("saveworld");
            await Task.Delay(5000);
        }

        StatusChanged?.Invoke(Loc.T("Restart_Restarting"));
        await _process.StopAsync(_telnet, ScheduledShutdownTimeoutMs);
        _telnet.Disconnect();
        await Task.Delay(3000);

        if (await _process.StartAsync())
            _ = ConnectTelnetWhenReadyAsync(_telnet, _process);
        else
            StatusChanged?.Invoke("[WARN] " + Loc.T("Restart_StartFailed"));
    }

    // ── Crash restarts ──

    private void OnCrashed(int? exitCode)
    {
        // A countdown for a server that's gone has nothing left to do
        CancelCountdown(announce: false);

        if (!_settings.CrashRestartEnabled) return;

        lock (_gate)
        {
            if (_busy) return;
            if (!_crashPolicy.TryRecordAttempt(DateTime.Now))
            {
                StatusChanged?.Invoke("[WARN] " + Loc.F("Restart_CrashLoopGaveUp",
                    _crashPolicy.MaxAttempts, (int)_crashPolicy.Window.TotalMinutes));
                return;
            }
            _busy = true;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                StatusChanged?.Invoke(Loc.F("Restart_CrashRestarting", (int)CrashRestartDelay.TotalSeconds,
                    _crashPolicy.RecentAttempts(DateTime.Now), _crashPolicy.MaxAttempts));
                _telnet.Disconnect();
                await Task.Delay(CrashRestartDelay);

                // Someone may have started it by hand meanwhile
                if (_process.CurrentState != ServerState.Stopped) return;

                if (await _process.StartAsync())
                    _ = ConnectTelnetWhenReadyAsync(_telnet, _process);
                else
                    StatusChanged?.Invoke("[WARN] " + Loc.T("Restart_StartFailed"));
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("[WARN] " + Loc.F("Restart_Failed", ex.Message));
            }
            finally
            {
                lock (_gate) _busy = false;
                Reschedule();
            }
        });
    }

    // ── Helpers ──

    /// <summary>
    /// Connect telnet once the server is ready to accept it. A 3.x server can take a few
    /// minutes to boot, so keep trying for a while instead of one fixed wait.
    /// </summary>
    public static async Task ConnectTelnetWhenReadyAsync(TelnetService telnet, ServerProcessService process)
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (process.CurrentState != ServerState.Running || telnet.IsConnected) return;
            // Only "not up yet" is worth retrying. A wrong password stays wrong, and every
            // try counts toward the server's TelnetFailedLoginLimit block.
            if (await telnet.ConnectAsync() != TelnetConnectResult.Unreachable) return;
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
    }

    private async Task EnsureTelnetAsync()
    {
        if (!_telnet.IsConnected)
            await _telnet.ConnectAsync();
        if (!_telnet.IsConnected)
            StatusChanged?.Invoke("[WARN] " + Loc.T("Restart_NoTelnet"));
    }

    private async Task SayAsync(string message)
    {
        if (!_telnet.IsConnected) return;
        try { await _telnet.SendCommandAsync(RestartSchedule.SayCommand(message)); }
        catch { /* a missed warning must not stop the restart */ }
    }

    private static async Task DelayUntilAsync(DateTime when, CancellationToken token)
    {
        var wait = when - DateTime.Now;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, token);
        token.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _countdown?.Cancel();
    }
}
