using System.IO;
using Kitsune7Den.Models;
using Kitsune7Den.Services;

namespace Kitsune7Den.Tests;

public class RestartScheduleTests
{
    private static DateTime At(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0);

    [Theory]
    [InlineData("04:00", new[] { "04:00" })]
    [InlineData("4:00", new[] { "04:00" })]
    [InlineData("16:30, 04:00", new[] { "04:00", "16:30" })]
    [InlineData("04:00;12:00 20:00", new[] { "04:00", "12:00", "20:00" })]
    [InlineData("04:00，16:00", new[] { "04:00", "16:00" })] // full-width comma from a Chinese IME
    [InlineData("04：00", new[] { "04:00" })]                 // full-width colon
    [InlineData("04:00, 04:00", new[] { "04:00" })]
    public void ParseTimes_AcceptsCommonFormats(string text, string[] expected)
    {
        var times = RestartSchedule.ParseTimes(text);
        Assert.NotNull(times);
        Assert.Equal(expected, times!.Select(t => t.ToString("HH:mm")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("25:00")]
    [InlineData("4am")]
    [InlineData("04:00, nope")]
    [InlineData(null)]
    public void ParseTimes_RejectsGarbage(string? text)
    {
        Assert.Null(RestartSchedule.ParseTimes(text));
    }

    [Fact]
    public void ParseWarnings_SortsLongestFirst_AndDedupes()
    {
        Assert.Equal([10, 5, 1], RestartSchedule.ParseWarnings("1, 10, 5, 5"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ParseWarnings_BlankMeansNoWarnings(string? text)
    {
        Assert.Empty(RestartSchedule.ParseWarnings(text)!);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("121")]
    [InlineData("-5")]
    [InlineData("5 min")]
    [InlineData("2.5")]
    public void ParseWarnings_RejectsOutOfRange(string text)
    {
        Assert.Null(RestartSchedule.ParseWarnings(text));
    }

    [Fact]
    public void NextDaily_LaterToday()
    {
        var times = RestartSchedule.ParseTimes("04:00, 16:00")!;
        Assert.Equal(At(14, 16, 0), RestartSchedule.NextDaily(At(14, 9, 30), times));
    }

    [Fact]
    public void NextDaily_RollsToTomorrowsFirstTime()
    {
        var times = RestartSchedule.ParseTimes("04:00, 16:00")!;
        Assert.Equal(At(15, 4, 0), RestartSchedule.NextDaily(At(14, 17, 0), times));
    }

    [Fact]
    public void NextDaily_IsStrictlyAfterNow()
    {
        // Right after a 04:00 restart, the next one is tomorrow, not "now" again
        var times = RestartSchedule.ParseTimes("04:00")!;
        Assert.Equal(At(15, 4, 0), RestartSchedule.NextDaily(At(14, 4, 0), times));
    }

    [Fact]
    public void NextDaily_FromASkippedOccurrence_GoesToTheOneAfter()
    {
        // Cancelling the 04:00 countdown resumes scheduling after it
        var times = RestartSchedule.ParseTimes("04:00, 16:00")!;
        Assert.Equal(At(14, 16, 0), RestartSchedule.NextDaily(At(14, 4, 0), times));
    }

    [Fact]
    public void NextInterval_CountsFromServerStart()
    {
        Assert.Equal(At(14, 18, 0), RestartSchedule.NextInterval(At(14, 12, 0), 6, At(14, 13, 0), leadMinutes: 1));
    }

    [Fact]
    public void NextInterval_OverdueServer_GetsAWarningWindowInsteadOfAnInstantRestart()
    {
        var now = At(14, 20, 0);
        Assert.Equal(now.AddMinutes(10), RestartSchedule.NextInterval(At(14, 6, 0), 6, now, leadMinutes: 10));
    }

    [Fact]
    public void WarningsToSend_SkipsWarningsThatAreAlreadyPast()
    {
        Assert.Equal([5, 1], RestartSchedule.WarningsToSend([10, 5, 1], TimeSpan.FromMinutes(6)));
        Assert.Equal([10, 5, 1], RestartSchedule.WarningsToSend([10, 5, 1], TimeSpan.FromMinutes(10)));
        Assert.Empty(RestartSchedule.WarningsToSend([10, 5, 1], TimeSpan.Zero));
    }

    [Fact]
    public void WarningsToSend_RoundsPartialMinutesUp()
    {
        // A countdown starting at 9m40s still gets its "10 minutes" warning
        Assert.Equal([10, 5], RestartSchedule.WarningsToSend([10, 5], TimeSpan.FromSeconds(580)));
    }

    [Fact]
    public void FormatWarning_FillsMinutes_AndFallsBackWhenBlank()
    {
        Assert.Equal("Back in 5!", RestartSchedule.FormatWarning("Back in {minutes}!", 5, "fallback {minutes}"));
        Assert.Equal("fallback 5", RestartSchedule.FormatWarning("  ", 5, "fallback {minutes}"));
    }

    [Fact]
    public void SayCommand_QuotesTheMessage_AndNeutralizesQuotesAndNewlines()
    {
        Assert.Equal("say \"Restart in 5\"", RestartSchedule.SayCommand("Restart in 5"));
        Assert.Equal("say \"It's 'soon' now\"", RestartSchedule.SayCommand("It's \"soon\"\nnow"));
    }

    [Fact]
    public void CrashPolicy_AllowsMaxAttemptsInWindow_ThenRefuses()
    {
        var policy = new CrashRestartPolicy(3, TimeSpan.FromMinutes(15));
        var t = At(14, 10, 0);

        Assert.True(policy.TryRecordAttempt(t));
        Assert.True(policy.TryRecordAttempt(t.AddMinutes(1)));
        Assert.True(policy.TryRecordAttempt(t.AddMinutes(2)));
        Assert.False(policy.TryRecordAttempt(t.AddMinutes(3)));
        Assert.Equal(3, policy.RecentAttempts(t.AddMinutes(3)));
    }

    [Fact]
    public void CrashPolicy_ForgetsAttemptsOutsideTheWindow()
    {
        var policy = new CrashRestartPolicy(2, TimeSpan.FromMinutes(15));
        var t = At(14, 10, 0);
        policy.TryRecordAttempt(t);
        policy.TryRecordAttempt(t.AddMinutes(1));

        // A server that ran fine for a while gets a fresh budget
        Assert.True(policy.TryRecordAttempt(t.AddMinutes(20)));
    }
}

/// <summary>
/// Crash detection against a real process. A copy of Windows' ping.exe stands in for the
/// server: given the server's launch arguments it prints usage and exits by itself, which
/// is exactly what a crash looks like from outside.
/// </summary>
[Collection("Localization")]
public class ServerCrashDetectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "Kitsune7DenCrash_" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task ProcessExitingOnItsOwn_RaisesCrashed_AndEndsStopped()
    {
        Directory.CreateDirectory(_dir);
        var fakeServer = Path.Combine(_dir, "FakeServer.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), fakeServer);

        var settings = new AppSettings { ServerExePath = fakeServer, ServerDirectory = _dir };
        using var process = new ServerProcessService(settings, new SteamCmdService(settings));
        var crashed = new TaskCompletionSource<int?>();
        process.Crashed += code => crashed.TrySetResult(code);

        await process.StartAsync();
        var finished = await Task.WhenAny(crashed.Task, Task.Delay(TimeSpan.FromSeconds(15)));

        Assert.Same(crashed.Task, finished);
        Assert.NotEqual(0, await crashed.Task); // ping rejects -logfile
        Assert.Equal(ServerState.Stopped, process.CurrentState);
        Assert.Null(process.StartedAt);
    }
}
