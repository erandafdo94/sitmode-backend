namespace FocusRouter.Api.Domain;

// One row per habit per day it was checked off. Date is the user's LOCAL date
// (YYYY-MM-DD), matching the frontend's dateKey() convention, so "what day is it"
// is unambiguous across timezones. (HabitId, Date) is unique — check-ins are
// idempotent toggles, not an append-only log.
public class HabitCompletion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HabitId { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Habit? Habit { get; set; }
}
