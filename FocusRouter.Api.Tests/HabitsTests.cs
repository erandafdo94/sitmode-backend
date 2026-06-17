using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FocusRouter.Api.Habits;

namespace FocusRouter.Api.Tests;

// End-to-end coverage of the structured habit API: create, check-in (idempotent +
// toggle off), server-computed streak/weekly stats, and per-user scoping. Reuses
// the in-memory TestAppFactory from the auth tests.
public class HabitsApiTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public HabitsApiTests(TestAppFactory factory) => _factory = factory;

    // Register a fresh user (unique email per test — the factory's in-memory DB is
    // shared across a class) and return a bearer-authed client.
    private async Task<HttpClient> SignedInClient(string email)
    {
        var client = _factory.CreateClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var token = JsonDocument.Parse(await reg.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Habits_RequireAuth()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/habits")).StatusCode);
    }

    [Fact]
    public async Task Daily_Create_CheckIn_Toggle()
    {
        var client = await SignedInClient("habit-daily@example.com");
        const string today = "2026-06-16";

        var create = await client.PostAsJsonAsync("/api/habits", new { name = "Meditate", kind = "Daily" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var habit = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement;
        var id = habit.GetProperty("id").GetString()!;
        Assert.Equal("Daily", habit.GetProperty("kind").GetString());
        Assert.Equal(1, habit.GetProperty("targetCount").GetInt32());
        Assert.False(habit.GetProperty("doneToday").GetBoolean());
        Assert.Equal(0, habit.GetProperty("currentStreak").GetInt32());

        // Check in today -> doneToday + streak 1.
        var checkin = await client.PostAsJsonAsync($"/api/habits/{id}/checkins?today={today}", new { date = today });
        Assert.Equal(HttpStatusCode.OK, checkin.StatusCode);
        var afterCheckin = JsonDocument.Parse(await checkin.Content.ReadAsStringAsync()).RootElement;
        Assert.True(afterCheckin.GetProperty("doneToday").GetBoolean());
        Assert.Equal(1, afterCheckin.GetProperty("currentStreak").GetInt32());
        Assert.Equal(1, afterCheckin.GetProperty("thisWeekCount").GetInt32());

        // Checking in again on the same day is idempotent (still 1, no duplicate).
        var again = await client.PostAsJsonAsync($"/api/habits/{id}/checkins?today={today}", new { date = today });
        Assert.Equal(1, JsonDocument.Parse(await again.Content.ReadAsStringAsync()).RootElement.GetProperty("thisWeekCount").GetInt32());

        // List reflects the single habit.
        var list = await client.GetAsync($"/api/habits?today={today}");
        Assert.Equal(1, JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement.GetArrayLength());

        // Toggle the check-in off.
        var del = await client.DeleteAsync($"/api/habits/{id}/checkins/{today}?today={today}");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        Assert.False(JsonDocument.Parse(await del.Content.ReadAsStringAsync()).RootElement.GetProperty("doneToday").GetBoolean());
    }

    [Fact]
    public async Task Weekly_Target_Met_GivesStreakAndWeekCount()
    {
        var client = await SignedInClient("habit-weekly@example.com");
        // 2026-06-15 is a Monday; the ISO week runs 06-15 .. 06-21.
        const string today = "2026-06-17";

        var create = await client.PostAsJsonAsync("/api/habits", new { name = "Workout", kind = "Weekly", targetCount = 3 });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        foreach (var d in new[] { "2026-06-15", "2026-06-16", "2026-06-17" })
            await client.PostAsJsonAsync($"/api/habits/{id}/checkins?today={today}", new { date = d });

        var list = await client.GetAsync($"/api/habits?today={today}");
        var h = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement[0];
        Assert.Equal(3, h.GetProperty("targetCount").GetInt32());
        Assert.Equal(3, h.GetProperty("thisWeekCount").GetInt32());
        Assert.Equal(3, h.GetProperty("weekDates").GetArrayLength());
        Assert.True(h.GetProperty("doneToday").GetBoolean());
        // Current week met the target of 3 -> a 1-week streak.
        Assert.Equal(1, h.GetProperty("currentStreak").GetInt32());
    }

    [Fact]
    public async Task Habits_AreScopedPerUser()
    {
        var alice = await SignedInClient("alice-habits@example.com");
        var create = await alice.PostAsJsonAsync("/api/habits", new { name = "Private", kind = "Daily" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var bob = await SignedInClient("bob-habits@example.com");
        var bobList = await bob.GetAsync("/api/habits?today=2026-06-16");
        Assert.Equal(0, JsonDocument.Parse(await bobList.Content.ReadAsStringAsync()).RootElement.GetArrayLength());

        // Bob cannot touch Alice's habit.
        var bobCheckin = await bob.PostAsJsonAsync($"/api/habits/{id}/checkins", new { date = "2026-06-16" });
        Assert.Equal(HttpStatusCode.NotFound, bobCheckin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/habits/{id}")).StatusCode);
    }

    [Fact]
    public async Task History_ReturnsAllCheckinDates_Sorted_AndScoped()
    {
        var client = await SignedInClient("habit-history@example.com");
        var create = await client.PostAsJsonAsync("/api/habits", new { name = "Read", kind = "Daily" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Retroactively mark a few non-contiguous past dates (out of order on purpose).
        foreach (var d in new[] { "2026-06-10", "2026-06-12", "2026-05-30" })
            await client.PostAsJsonAsync($"/api/habits/{id}/checkins", new { date = d });

        var hist = await client.GetAsync($"/api/habits/{id}/history");
        Assert.Equal(HttpStatusCode.OK, hist.StatusCode);
        var dates = JsonDocument.Parse(await hist.Content.ReadAsStringAsync())
            .RootElement.GetProperty("dates").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "2026-05-30", "2026-06-10", "2026-06-12" }, dates); // ascending

        // Another user cannot read this habit's history.
        var other = await SignedInClient("habit-history-2@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/habits/{id}/history")).StatusCode);
    }

    [Fact]
    public async Task CheckIn_FutureDate_IsRejected()
    {
        var client = await SignedInClient("habit-future@example.com");
        var create = await client.PostAsJsonAsync("/api/habits", new { name = "Stretch", kind = "Daily" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Relative to the client's today (2026-06-16), 2026-06-20 is the future.
        var future = await client.PostAsJsonAsync($"/api/habits/{id}/checkins?today=2026-06-16", new { date = "2026-06-20" });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);

        // Today itself is still allowed, and the response now carries recentDates.
        var ok = await client.PostAsJsonAsync($"/api/habits/{id}/checkins?today=2026-06-16", new { date = "2026-06-16" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var dto = JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains("2026-06-16", dto.GetProperty("recentDates").EnumerateArray().Select(e => e.GetString()));
    }
}

// Pure unit tests for the streak/progress math. Dates are built relative to the
// ISO week of a fixed `today`, so they don't depend on knowing weekdays by hand.
public class HabitStatsTests
{
    private static readonly DateOnly Today = new(2026, 6, 17);

    [Fact]
    public void Daily_CurrentStreak_CountsConsecutiveDaysEndingToday()
    {
        var done = new HashSet<DateOnly> { Today, Today.AddDays(-1), Today.AddDays(-2) };
        Assert.Equal(3, HabitStats.DailyCurrentStreak(done, Today));
    }

    [Fact]
    public void Daily_CurrentStreak_TodayNotDoneYet_IsGraced()
    {
        // Today not checked, but yesterday + the day before are — streak holds at 2.
        var done = new HashSet<DateOnly> { Today.AddDays(-1), Today.AddDays(-2) };
        Assert.Equal(2, HabitStats.DailyCurrentStreak(done, Today));
    }

    [Fact]
    public void Daily_CurrentStreak_GapBreaksIt()
    {
        var done = new HashSet<DateOnly> { Today, Today.AddDays(-1), Today.AddDays(-3) };
        Assert.Equal(2, HabitStats.DailyCurrentStreak(done, Today));
        Assert.Equal(2, HabitStats.DailyLongestStreak(done));
    }

    [Fact]
    public void Weekly_CurrentStreak_InProgressWeekDoesNotBreak()
    {
        var ws = HabitStats.WeekStart(Today);
        var done = new HashSet<DateOnly>();
        // Previous two weeks each hit the target of 3.
        foreach (var wk in new[] { -2, -1 })
        {
            var mon = ws.AddDays(7 * wk);
            done.Add(mon); done.Add(mon.AddDays(1)); done.Add(mon.AddDays(2));
        }
        // Current week only has 1 (target not yet met) — must not break the streak.
        done.Add(ws);
        Assert.Equal(2, HabitStats.WeeklyCurrentStreak(done, Today, 3));

        // Finish the current week's target -> streak becomes 3.
        done.Add(ws.AddDays(1)); done.Add(ws.AddDays(2));
        Assert.Equal(3, HabitStats.WeeklyCurrentStreak(done, Today, 3));
        Assert.Equal(3, HabitStats.WeeklyLongestStreak(done, 3));
    }
}
