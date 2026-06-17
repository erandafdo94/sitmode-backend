namespace FocusRouter.Api.Domain;

// Two shapes the user asked for:
//  - Daily:  do it every day. Streak = consecutive days. TargetCount is 1.
//  - Weekly: "N times per week" (e.g. work out 3×/week). Any days count;
//            streak = consecutive weeks that hit TargetCount.
public enum HabitKind { Daily, Weekly }

public class Habit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // FK -> users.id (cascade). Every query is scoped to the signed-in user.
    public Guid UserId { get; set; }
    public string Name { get; set; } = default!;
    public HabitKind Kind { get; set; } = HabitKind.Daily;
    // Weekly: target completions per ISO week. Daily: always 1.
    public int TargetCount { get; set; } = 1;
    // Reminders-style identity colour (hex) + optional emoji, for the UI.
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<HabitCompletion> Completions { get; set; } = new();
}
