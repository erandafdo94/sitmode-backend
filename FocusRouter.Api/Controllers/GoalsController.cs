using FocusRouter.Api.Auth;
using FocusRouter.Api.Data;
using FocusRouter.Api.Domain;
using FocusRouter.Api.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FocusRouter.Api.Controllers;

// The goal ladder: 25-year vision -> 5-year horizon -> yearly SMART goals ->
// monthly/weekly cadence. All five horizons are rows in one self-referencing
// table scoped to the signed-in user. Progress is a stored number (current /
// target), so unlike habits there are no check-in rows — ProgressPct is computed
// at read time. Cadence goals are "completed" by flipping Status.
[ApiController]
[Route("api/goals")]
[Authorize]
public class GoalsController : ControllerBase
{
    private readonly AppDbContext _db;

    public GoalsController(AppDbContext db) => _db = db;

    // GET /api/goals[?horizon=Year&archived=true]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? horizon, [FromQuery] bool archived, CancellationToken ct)
    {
        var uid = User.UserId();
        var q = _db.Goals.Where(g => g.UserId == uid && (archived || !g.Archived));
        if (horizon is not null)
        {
            if (!TryParseHorizon(horizon, out var h)) return BadRequest(new { error = "invalid horizon" });
            q = q.Where(g => g.Horizon == h);
        }
        var goals = await q.OrderBy(g => g.SortOrder).ThenBy(g => g.CreatedAt).ToListAsync(ct);
        return Ok(goals.Select(ToDto).ToList());
    }

    // POST /api/goals
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGoalRequest body, CancellationToken ct)
    {
        var uid = User.UserId();
        var title = (body.Title ?? "").Trim();
        if (title.Length == 0) return BadRequest(new { error = "title required" });
        if (!TryParseHorizon(body.Horizon, out var horizon)) return BadRequest(new { error = "invalid horizon" });
        if (body.ParentGoalId is Guid pid && !await OwnsAsync(pid, uid, ct))
            return BadRequest(new { error = "parent goal not found" });

        var maxSort = await _db.Goals.Where(g => g.UserId == uid).Select(g => (int?)g.SortOrder).MaxAsync(ct) ?? -1;
        var goal = new Goal
        {
            UserId = uid,
            Title = title,
            Description = Clean(body.Description),
            Horizon = horizon,
            ParentGoalId = body.ParentGoalId,
            TargetValue = body.TargetValue,
            CurrentValue = body.CurrentValue,
            Unit = Clean(body.Unit),
            DueDate = body.DueDate,
            Color = Clean(body.Color),
            Icon = Clean(body.Icon),
            SortOrder = maxSort + 1,
        };
        _db.Goals.Add(goal);
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(goal));
    }

    // PUT /api/goals/{id} — PATCH-style; only provided fields change.
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGoalRequest body, CancellationToken ct)
    {
        var uid = User.UserId();
        var goal = await Owned(id, uid, ct);
        if (goal is null) return NotFound();

        if (body.Title is not null)
        {
            var title = body.Title.Trim();
            if (title.Length == 0) return BadRequest(new { error = "title required" });
            goal.Title = title;
        }
        if (body.Description is not null) goal.Description = Clean(body.Description);
        if (body.Horizon is not null)
        {
            if (!TryParseHorizon(body.Horizon, out var h)) return BadRequest(new { error = "invalid horizon" });
            goal.Horizon = h;
        }
        if (body.ParentGoalId is Guid pid)
        {
            // A goal cannot be its own parent, and the parent must be the caller's.
            if (pid == goal.Id || !await OwnsAsync(pid, uid, ct))
                return BadRequest(new { error = "parent goal not found" });
            goal.ParentGoalId = pid;
        }
        if (body.Status is not null)
        {
            if (!TryParseStatus(body.Status, out var s)) return BadRequest(new { error = "invalid status" });
            // Stamp the achievement time on the transition into Completed; clear it
            // if the goal moves back out of Completed.
            if (s == GoalStatus.Completed && goal.Status != GoalStatus.Completed)
                goal.CompletedAt = DateTimeOffset.UtcNow;
            else if (s != GoalStatus.Completed)
                goal.CompletedAt = null;
            goal.Status = s;
        }
        if (body.TargetValue is not null) goal.TargetValue = body.TargetValue;
        if (body.CurrentValue is not null) goal.CurrentValue = body.CurrentValue;
        if (body.Unit is not null) goal.Unit = Clean(body.Unit);
        if (body.DueDate is not null) goal.DueDate = body.DueDate;
        if (body.Color is not null) goal.Color = Clean(body.Color);
        if (body.Icon is not null) goal.Icon = Clean(body.Icon);
        if (body.SortOrder is not null) goal.SortOrder = body.SortOrder.Value;
        if (body.Archived is not null) goal.Archived = body.Archived.Value;
        goal.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(goal));
    }

    // DELETE /api/goals/{id} — children are orphaned (ParentGoalId -> null), not deleted.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var uid = User.UserId();
        var goal = await Owned(id, uid, ct);
        if (goal is null) return NotFound();
        _db.Goals.Remove(goal);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- helpers ----

    // Loads a goal scoped to the caller — returns null (=> 404) for unknown ids
    // and for goals owned by another user.
    private Task<Goal?> Owned(Guid id, Guid uid, CancellationToken ct) =>
        _db.Goals.FirstOrDefaultAsync(g => g.Id == id && g.UserId == uid, ct);

    private Task<bool> OwnsAsync(Guid id, Guid uid, CancellationToken ct) =>
        _db.Goals.AnyAsync(g => g.Id == id && g.UserId == uid, ct);

    private static string? Clean(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private static bool TryParseHorizon(string? raw, out GoalHorizon horizon)
    {
        horizon = GoalHorizon.Year;
        return !string.IsNullOrWhiteSpace(raw)
            && Enum.TryParse(raw, ignoreCase: true, out horizon) && Enum.IsDefined(horizon);
    }

    private static bool TryParseStatus(string? raw, out GoalStatus status)
    {
        status = GoalStatus.Active;
        return !string.IsNullOrWhiteSpace(raw)
            && Enum.TryParse(raw, ignoreCase: true, out status) && Enum.IsDefined(status);
    }

    private static GoalDto ToDto(Goal g)
    {
        int? pct = g.TargetValue is double t && t > 0 && g.CurrentValue is double c
            ? Math.Clamp((int)Math.Round(c / t * 100), 0, 100)
            : null;
        return new GoalDto(
            g.Id, g.Title, g.Description, g.Horizon.ToString(), g.ParentGoalId,
            g.TargetValue, g.CurrentValue, g.Unit, g.DueDate, g.Status.ToString(),
            g.Color, g.Icon, g.SortOrder, g.Archived, g.CreatedAt, g.CompletedAt, pct);
    }
}
