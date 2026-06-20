namespace FocusRouter.Api.Dto;

// Client sends Horizon as "Vision25"/"Horizon5"/"Year"/"Month"/"Week". Target/
// Current/Unit/DueDate are the SMART fields (omitted for the vision tiers).
// ParentGoalId, when set, must reference a goal owned by the same user.
public record CreateGoalRequest(
    string Title,
    string? Description,
    string Horizon,
    Guid? ParentGoalId,
    double? TargetValue,
    double? CurrentValue,
    string? Unit,
    DateOnly? DueDate,
    string? Color,
    string? Icon);

// PATCH-style: only non-null fields are applied. Status is sent as
// "Active"/"Completed"/"Abandoned"; cadence rows flip to Completed via this.
public record UpdateGoalRequest(
    string? Title,
    string? Description,
    string? Horizon,
    Guid? ParentGoalId,
    double? TargetValue,
    double? CurrentValue,
    string? Unit,
    DateOnly? DueDate,
    string? Status,
    string? Color,
    string? Icon,
    int? SortOrder,
    bool? Archived);

// Goal plus the server-computed progress percent. ASP.NET serializes records as
// camelCase (id, parentGoalId, targetValue, progressPct …), which the frontend
// consumes. ProgressPct is null when there's no numeric target (vision tiers).
public record GoalDto(
    Guid Id,
    string Title,
    string? Description,
    string Horizon,
    Guid? ParentGoalId,
    double? TargetValue,
    double? CurrentValue,
    string? Unit,
    DateOnly? DueDate,
    string Status,
    string? Color,
    string? Icon,
    int SortOrder,
    bool Archived,
    int? ProgressPct);
