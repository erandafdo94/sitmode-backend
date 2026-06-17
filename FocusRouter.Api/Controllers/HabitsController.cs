using FocusRouter.Api.Auth;
using FocusRouter.Api.Data;
using FocusRouter.Api.Domain;
using FocusRouter.Api.Dto;
using FocusRouter.Api.Habits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FocusRouter.Api.Controllers;

// Structured habit tracker. Unlike /api/state (an opaque per-user blob), habits
// are first-class rows scoped to the signed-in user, and streaks/weekly progress
// are computed server-side at read time. The client passes its local `today` so
// "current streak" and week boundaries reflect the user's day, not server UTC.
[ApiController]
[Route("api/habits")]
[Authorize]
public class HabitsController : ControllerBase
{
    private readonly AppDbContext _db;

    public HabitsController(AppDbContext db) => _db = db;

    // GET /api/habits?today=YYYY-MM-DD[&archived=true]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateOnly? today, [FromQuery] bool archived, CancellationToken ct)
    {
        var uid = User.UserId();
        var habits = await _db.Habits
            .Where(h => h.UserId == uid && (archived || !h.Archived))
            .Include(h => h.Completions)
            .OrderBy(h => h.SortOrder).ThenBy(h => h.CreatedAt)
            .ToListAsync(ct);
        var t = today ?? Today();
        return Ok(habits.Select(h => ToDto(h, t)).ToList());
    }

    // POST /api/habits
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateHabitRequest body, [FromQuery] DateOnly? today, CancellationToken ct)
    {
        var uid = User.UserId();
        var name = (body.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { error = "name required" });
        if (!TryParseKind(body.Kind, out var kind)) return BadRequest(new { error = "kind must be Daily or Weekly" });

        var maxSort = await _db.Habits.Where(h => h.UserId == uid).Select(h => (int?)h.SortOrder).MaxAsync(ct) ?? -1;
        var habit = new Habit
        {
            UserId = uid,
            Name = name,
            Kind = kind,
            TargetCount = NormalizeTarget(kind, body.TargetCount),
            Color = Clean(body.Color),
            Icon = Clean(body.Icon),
            SortOrder = maxSort + 1,
        };
        _db.Habits.Add(habit);
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(habit, today ?? Today()));
    }

    // PUT /api/habits/{id} — PATCH-style; only provided fields change.
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateHabitRequest body, [FromQuery] DateOnly? today, CancellationToken ct)
    {
        var habit = await Owned(id, ct);
        if (habit is null) return NotFound();

        if (body.Name is not null)
        {
            var name = body.Name.Trim();
            if (name.Length == 0) return BadRequest(new { error = "name required" });
            habit.Name = name;
        }
        if (body.Kind is not null)
        {
            if (!TryParseKind(body.Kind, out var kind)) return BadRequest(new { error = "kind must be Daily or Weekly" });
            habit.Kind = kind;
            habit.TargetCount = NormalizeTarget(kind, body.TargetCount ?? habit.TargetCount);
        }
        else if (body.TargetCount is not null)
        {
            habit.TargetCount = NormalizeTarget(habit.Kind, body.TargetCount);
        }
        if (body.Color is not null) habit.Color = Clean(body.Color);
        if (body.Icon is not null) habit.Icon = Clean(body.Icon);
        if (body.SortOrder is not null) habit.SortOrder = body.SortOrder.Value;
        if (body.Archived is not null) habit.Archived = body.Archived.Value;
        habit.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(habit, today ?? Today()));
    }

    // DELETE /api/habits/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var habit = await Owned(id, ct);
        if (habit is null) return NotFound();
        _db.Habits.Remove(habit);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/habits/{id}/checkins  body { date? } — idempotent mark-done.
    [HttpPost("{id:guid}/checkins")]
    public async Task<IActionResult> CheckIn(Guid id, [FromBody] CheckinRequest? body, [FromQuery] DateOnly? today, CancellationToken ct)
    {
        var habit = await Owned(id, ct);
        if (habit is null) return NotFound();

        var date = body?.Date ?? today ?? Today();
        // No marking the future: compare against the client's local today when given.
        if (date > (today ?? Today())) return BadRequest(new { error = "cannot check in a future date" });
        if (!habit.Completions.Any(c => c.Date == date))
        {
            // Add via the DbSet (not the nav collection): the entity's Id is already
            // set by its initializer, so going through the collection makes
            // DetectChanges treat it as an existing row to UPDATE. DbSet.Add forces
            // the Added state; relationship fixup still populates habit.Completions.
            var completion = new HabitCompletion { HabitId = habit.Id, Date = date };
            _db.HabitCompletions.Add(completion);
            await _db.SaveChangesAsync(ct);
            if (!habit.Completions.Contains(completion)) habit.Completions.Add(completion);
        }
        return Ok(ToDto(habit, today ?? Today()));
    }

    // DELETE /api/habits/{id}/checkins/{date} — remove a check-in (toggle off).
    [HttpDelete("{id:guid}/checkins/{date}")]
    public async Task<IActionResult> RemoveCheckIn(Guid id, DateOnly date, [FromQuery] DateOnly? today, CancellationToken ct)
    {
        var habit = await Owned(id, ct);
        if (habit is null) return NotFound();

        var existing = habit.Completions.FirstOrDefault(c => c.Date == date);
        if (existing is not null)
        {
            _db.HabitCompletions.Remove(existing);
            habit.Completions.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
        return Ok(ToDto(habit, today ?? Today()));
    }

    // GET /api/habits/{id}/history — every check-in date for the habit (ascending),
    // for the calendar/heatmap view. Retroactive marking reuses the check-in
    // endpoints above, which already accept any date.
    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> History(Guid id, CancellationToken ct)
    {
        var habit = await Owned(id, ct);
        if (habit is null) return NotFound();
        var dates = habit.Completions
            .Select(c => c.Date)
            .OrderBy(d => d)
            .Select(d => d.ToString("yyyy-MM-dd"))
            .ToList();
        return Ok(new { dates });
    }

    // ---- helpers ----

    // Loads a habit with its completions, scoped to the caller — returns null
    // (=> 404) for unknown ids and for habits owned by another user.
    private Task<Habit?> Owned(Guid id, CancellationToken ct)
    {
        var uid = User.UserId();
        return _db.Habits.Include(h => h.Completions).FirstOrDefaultAsync(h => h.Id == id && h.UserId == uid, ct);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string? Clean(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private static bool TryParseKind(string? raw, out HabitKind kind)
    {
        if (string.IsNullOrWhiteSpace(raw)) { kind = HabitKind.Daily; return true; }
        return Enum.TryParse(raw, ignoreCase: true, out kind) && Enum.IsDefined(kind);
    }

    // Daily habits are always target 1; Weekly clamps to a sane 1..7 (default 3).
    private static int NormalizeTarget(HabitKind kind, int? target) =>
        kind == HabitKind.Daily ? 1 : Math.Clamp(target ?? 3, 1, 7);

    private static HabitDto ToDto(Habit h, DateOnly today)
    {
        var done = h.Completions.Select(c => c.Date).ToHashSet();
        var weekDates = HabitStats.ThisWeekDates(done, today);
        var recentDates = done.Where(d => d >= today.AddDays(-6) && d <= today)
            .OrderBy(d => d).Select(d => d.ToString("yyyy-MM-dd")).ToList();
        int current = h.Kind == HabitKind.Weekly
            ? HabitStats.WeeklyCurrentStreak(done, today, h.TargetCount)
            : HabitStats.DailyCurrentStreak(done, today);
        int longest = h.Kind == HabitKind.Weekly
            ? HabitStats.WeeklyLongestStreak(done, h.TargetCount)
            : HabitStats.DailyLongestStreak(done);
        return new HabitDto(
            h.Id, h.Name, h.Kind.ToString(), h.TargetCount, h.Color, h.Icon, h.SortOrder, h.Archived,
            DoneToday: done.Contains(today),
            CurrentStreak: current,
            LongestStreak: longest,
            ThisWeekCount: weekDates.Count,
            WeekDates: weekDates.Select(d => d.ToString("yyyy-MM-dd")).ToList(),
            RecentDates: recentDates);
    }
}
