namespace FocusRouter.Api.Domain;

// A single self-referencing table holds all five horizons of the goal ladder.
// `Horizon` distinguishes the tier; `ParentGoalId` is the up-link to a goal one
// tier higher (e.g. a Week goal -> a Year goal -> a Horizon5 -> the Vision25).
//   - Vision25 / Horizon5: aspirational north stars, no numeric target.
//   - Year / Month / Week: SMART goals with a measurable Target/Current + due date.
public enum GoalHorizon { Vision25, Horizon5, Year, Month, Week }

// Active goals are in flight; Completed/Abandoned are terminal. Cadence rows
// (Month/Week) flip to Completed instead of having check-in history.
public enum GoalStatus { Active, Completed, Abandoned }

public class Goal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // FK -> users.id (cascade). Every query is scoped to the signed-in user.
    public Guid UserId { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public GoalHorizon Horizon { get; set; } = GoalHorizon.Year;
    // Up-link to a higher-horizon goal owned by the same user. Null at the top
    // (Vision25) or whenever a goal is left unlinked. SET NULL on parent delete.
    public Guid? ParentGoalId { get; set; }
    // SMART "measurable": target/current numeric progress (e.g. 1000 km, 612 done).
    // Null for the vision tiers, which have no metric.
    public double? TargetValue { get; set; }
    public double? CurrentValue { get; set; }
    public string? Unit { get; set; }            // "km", "$", "customers"
    // SMART "time-bound".
    public DateOnly? DueDate { get; set; }
    public GoalStatus Status { get; set; } = GoalStatus.Active;
    // Set when the goal transitions to Completed (cleared if it leaves Completed).
    // Drives the achievement history / "achieved over time" view on the client.
    public DateTimeOffset? CompletedAt { get; set; }
    // Reminders-style identity colour (hex) + optional emoji, for the UI.
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
