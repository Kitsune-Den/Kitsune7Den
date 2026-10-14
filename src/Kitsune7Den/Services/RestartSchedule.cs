using System.Globalization;

namespace Kitsune7Den.Services;

public enum RestartScheduleMode
{
    /// <summary>At fixed local times of day ("04:00, 16:00").</summary>
    Daily,
    /// <summary>After the server has been up for N hours.</summary>
    Interval,
}

/// <summary>
/// The pure scheduling math behind automatic restarts — no timers, no I/O — so it can be
/// tested against fixed clocks. All times are local: Kitsune7Den runs on the server box,
/// so "04:00" means 04:00 where the server is.
/// </summary>
public static class RestartSchedule
{
    /// <summary>
    /// "04:00, 16:30" → sorted distinct times. Accepts commas, semicolons or spaces between
    /// entries and H:mm or HH:mm. Null if anything doesn't parse, or nothing was given.
    /// </summary>
    public static IReadOnlyList<TimeOnly>? ParseTimes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var times = new SortedSet<TimeOnly>();
        foreach (var part in text.Split([',', ';', ' ', '，', '；'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TimeOnly.TryParseExact(part.Replace('：', ':'), ["H:mm", "HH:mm"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return null;
            times.Add(time);
        }
        return times.Count > 0 ? times.ToList() : null;
    }

    /// <summary>
    /// "10, 5, 1" → warning lead times in minutes, longest first. Each must be 1–120.
    /// Empty text means no warnings (restart without notice). Null if anything is invalid.
    /// </summary>
    public static IReadOnlyList<int>? ParseWarnings(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var minutes = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
        foreach (var part in text.Split([',', ';', ' ', '，', '；'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var m) || m is < 1 or > 120)
                return null;
            minutes.Add(m);
        }
        return minutes.ToList();
    }

    /// <summary>The next of <paramref name="times"/> strictly after <paramref name="now"/>.</summary>
    public static DateTime NextDaily(DateTime now, IReadOnlyList<TimeOnly> times)
    {
        if (times.Count == 0) throw new ArgumentException("At least one time is required.", nameof(times));

        var today = DateOnly.FromDateTime(now);
        foreach (var t in times.OrderBy(t => t))
        {
            var candidate = today.ToDateTime(t);
            if (candidate > now) return candidate;
        }
        return today.AddDays(1).ToDateTime(times.Min());
    }

    /// <summary>
    /// Interval mode: <paramref name="hours"/> after the server started. A server that has
    /// already been up longer than that (Kitsune7Den was closed, or the setting just changed)
    /// restarts after the shortest warning window rather than instantly.
    /// </summary>
    public static DateTime NextInterval(DateTime serverStartedAt, int hours, DateTime now, int leadMinutes)
    {
        var due = serverStartedAt.AddHours(hours);
        var earliest = now.AddMinutes(leadMinutes);
        return due > earliest ? due : earliest;
    }

    /// <summary>
    /// The warnings still worth sending when a countdown starts <paramref name="remaining"/>
    /// before the restart: a warning for "10 minutes" is skipped if only 3 are left.
    /// </summary>
    public static IReadOnlyList<int> WarningsToSend(IReadOnlyList<int> warnings, TimeSpan remaining) =>
        warnings.Where(m => m <= Math.Ceiling(remaining.TotalMinutes)).OrderByDescending(m => m).ToList();

    /// <summary>The in-game message, with {minutes} filled in. A blank template uses the fallback.</summary>
    public static string FormatWarning(string? template, int minutes, string fallback) =>
        (string.IsNullOrWhiteSpace(template) ? fallback : template)
        .Replace("{minutes}", minutes.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// A 7D2D <c>say</c> command. The game takes the text in double quotes and has no
    /// escape for them, so inner quotes become single quotes.
    /// </summary>
    public static string SayCommand(string message) =>
        $"say \"{message.Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ')}\"";
}

/// <summary>
/// Crash-loop guard: allow at most <see cref="MaxAttempts"/> automatic restarts within
/// <see cref="Window"/>. A server that dies straight after starting (a broken mod, a bad
/// config) then stays down instead of restarting forever.
/// </summary>
public sealed class CrashRestartPolicy
{
    private readonly Queue<DateTime> _attempts = new();

    public CrashRestartPolicy(int maxAttempts, TimeSpan window)
    {
        MaxAttempts = Math.Max(1, maxAttempts);
        Window = window;
    }

    public int MaxAttempts { get; }
    public TimeSpan Window { get; }

    /// <summary>How many restarts are inside the window right now.</summary>
    public int RecentAttempts(DateTime now)
    {
        while (_attempts.Count > 0 && now - _attempts.Peek() > Window)
            _attempts.Dequeue();
        return _attempts.Count;
    }

    /// <summary>Records and allows an attempt, or refuses when the budget is spent.</summary>
    public bool TryRecordAttempt(DateTime now)
    {
        if (RecentAttempts(now) >= MaxAttempts) return false;
        _attempts.Enqueue(now);
        return true;
    }

    public void Reset() => _attempts.Clear();
}
