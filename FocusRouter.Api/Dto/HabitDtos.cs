namespace FocusRouter.Api.Dto;

// Client sends Kind as "Daily"/"Weekly". TargetCount is the per-week goal for
// Weekly habits (ignored/forced to 1 for Daily).
public record CreateHabitRequest(string Name, string? Kind, int? TargetCount, string? Color, string? Icon);

// PATCH-style: only non-null fields are applied.
public record UpdateHabitRequest(string? Name, string? Kind, int? TargetCount,
                                 string? Color, string? Icon, int? SortOrder, bool? Archived);

// Date defaults to "today" server-side when omitted; the client sends its local date.
public record CheckinRequest(DateOnly? Date);

// Habit plus server-computed stats. ASP.NET serializes records as camelCase
// (id, doneToday, currentStreak, thisWeekCount, weekDates …), which is what the
// frontend consumes. WeekDates are YYYY-MM-DD strings done in the current ISO week.
public record HabitDto(
    Guid Id,
    string Name,
    string Kind,
    int TargetCount,
    string? Color,
    string? Icon,
    int SortOrder,
    bool Archived,
    bool DoneToday,
    int CurrentStreak,
    int LongestStreak,
    int ThisWeekCount,
    IReadOnlyList<string> WeekDates,
    // The dates done in the last 7 days (today + previous 6) — drives the inline
    // day strip on each row, which spans week boundaries unlike WeekDates.
    IReadOnlyList<string> RecentDates);
