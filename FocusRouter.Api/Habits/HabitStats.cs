using FocusRouter.Api.Domain;

namespace FocusRouter.Api.Habits;

// Pure streak/progress math, computed at read time from the set of completion
// dates. No DB access and no caching, so adding or removing a check-in can never
// desync a stored counter. Weeks are ISO (Monday-start); v1 hardcodes that (a
// per-user week-start could be layered on later).
public static class HabitStats
{
    // The Monday that starts the week containing `d`. DayOfWeek is Sun=0..Sat=6,
    // so (DayOfWeek + 6) % 7 is the number of days back to Monday (Mon=0..Sun=6).
    public static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    // Consecutive done-days ending at `today`. If today isn't done yet, anchor at
    // yesterday so an as-yet-unchecked today doesn't read as a broken streak.
    public static int DailyCurrentStreak(IReadOnlySet<DateOnly> done, DateOnly today)
    {
        if (done.Count == 0) return 0;
        var cursor = done.Contains(today) ? today : today.AddDays(-1);
        int n = 0;
        while (done.Contains(cursor)) { n++; cursor = cursor.AddDays(-1); }
        return n;
    }

    // Longest run of consecutive days anywhere in history.
    public static int DailyLongestStreak(IReadOnlySet<DateOnly> done)
    {
        if (done.Count == 0) return 0;
        var sorted = done.OrderBy(d => d).ToList();
        int best = 1, run = 1;
        for (int i = 1; i < sorted.Count; i++)
        {
            run = sorted[i] == sorted[i - 1].AddDays(1) ? run + 1 : 1;
            if (run > best) best = run;
        }
        return best;
    }

    // Completions per ISO week, keyed by the week's Monday.
    private static Dictionary<DateOnly, int> CountByWeek(IEnumerable<DateOnly> done)
    {
        var m = new Dictionary<DateOnly, int>();
        foreach (var d in done)
        {
            var w = WeekStart(d);
            m[w] = m.TryGetValue(w, out var n) ? n + 1 : 1;
        }
        return m;
    }

    // Consecutive weeks ending at the current week that hit `target`. The current
    // week only has to count if it has already met the target; if it hasn't yet,
    // it's "in progress" and the streak is measured from the previous week, so an
    // incomplete current week never reads as a break.
    public static int WeeklyCurrentStreak(IReadOnlySet<DateOnly> done, DateOnly today, int target)
    {
        if (target < 1 || done.Count == 0) return 0;
        var byWeek = CountByWeek(done);
        bool Met(DateOnly w) => byWeek.TryGetValue(w, out var n) && n >= target;

        var current = WeekStart(today);
        var cursor = Met(current) ? current : current.AddDays(-7);
        int n = 0;
        while (Met(cursor)) { n++; cursor = cursor.AddDays(-7); }
        return n;
    }

    // Longest run of consecutive met weeks anywhere in history.
    public static int WeeklyLongestStreak(IReadOnlySet<DateOnly> done, int target)
    {
        if (target < 1 || done.Count == 0) return 0;
        var metWeeks = CountByWeek(done)
            .Where(kv => kv.Value >= target)
            .Select(kv => kv.Key)
            .OrderBy(w => w)
            .ToList();
        if (metWeeks.Count == 0) return 0;
        int best = 1, run = 1;
        for (int i = 1; i < metWeeks.Count; i++)
        {
            run = metWeeks[i] == metWeeks[i - 1].AddDays(7) ? run + 1 : 1;
            if (run > best) best = run;
        }
        return best;
    }

    // The done-dates in the ISO week containing `today` (drives the M–S strip and
    // the weekly "N of target" count), sorted ascending.
    public static List<DateOnly> ThisWeekDates(IEnumerable<DateOnly> done, DateOnly today)
    {
        var ws = WeekStart(today);
        var we = ws.AddDays(7);
        return done.Where(d => d >= ws && d < we).OrderBy(d => d).ToList();
    }
}
