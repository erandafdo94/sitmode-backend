using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FocusRouter.Api.Tests;

// End-to-end coverage of the goal ladder API: create across horizons, the
// parent up-link (and its ownership guard), server-computed ProgressPct,
// PATCH-style updates, and per-user scoping. Reuses the in-memory TestAppFactory.
public class GoalsApiTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public GoalsApiTests(TestAppFactory factory) => _factory = factory;

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
    public async Task Goals_RequireAuth()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/goals")).StatusCode);
    }

    [Fact]
    public async Task Create_SmartGoal_ComputesProgressPct()
    {
        var client = await SignedInClient("goal-smart@example.com");

        var create = await client.PostAsJsonAsync("/api/goals", new
        {
            title = "Run 1000 km this year",
            horizon = "Year",
            targetValue = 1000,
            currentValue = 612,
            unit = "km",
            dueDate = "2026-12-31",
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var g = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Year", g.GetProperty("horizon").GetString());
        Assert.Equal("Active", g.GetProperty("status").GetString());
        Assert.Equal("km", g.GetProperty("unit").GetString());
        Assert.Equal(61, g.GetProperty("progressPct").GetInt32()); // round(612/1000*100)
    }

    [Fact]
    public async Task VisionGoal_WithoutTarget_HasNullProgress()
    {
        var client = await SignedInClient("goal-vision@example.com");
        var create = await client.PostAsJsonAsync("/api/goals", new
        {
            title = "Be financially free and teach what I've learned",
            horizon = "Vision25",
        });
        var g = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Null, g.GetProperty("progressPct").ValueKind);
    }

    [Fact]
    public async Task ParentLink_RollsUp_AndIsValidated()
    {
        var client = await SignedInClient("goal-ladder@example.com");

        // A 5-year horizon goal...
        var parent = await client.PostAsJsonAsync("/api/goals", new { title = "Ship a profitable product", horizon = "Horizon5" });
        var parentId = JsonDocument.Parse(await parent.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // ...a yearly goal that links up to it.
        var child = await client.PostAsJsonAsync("/api/goals", new
        {
            title = "Reach $2k MRR",
            horizon = "Year",
            parentGoalId = parentId,
            targetValue = 2000,
            currentValue = 740,
            unit = "$",
        });
        Assert.Equal(HttpStatusCode.OK, child.StatusCode);
        var childEl = JsonDocument.Parse(await child.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(parentId, childEl.GetProperty("parentGoalId").GetString());
        Assert.Equal(37, childEl.GetProperty("progressPct").GetInt32());

        // A bogus parent id is rejected.
        var bad = await client.PostAsJsonAsync("/api/goals", new
        {
            title = "Orphan",
            horizon = "Year",
            parentGoalId = Guid.NewGuid(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Update_IsPatchStyle_AndProgressRecomputes()
    {
        var client = await SignedInClient("goal-update@example.com");
        var create = await client.PostAsJsonAsync("/api/goals", new
        {
            title = "Read 12 books",
            horizon = "Year",
            targetValue = 12,
            currentValue = 3,
        });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Bump progress + mark a cadence-style completion; title untouched.
        var update = await client.PutAsJsonAsync($"/api/goals/{id}", new { currentValue = 6, status = "Completed" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var g = JsonDocument.Parse(await update.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Read 12 books", g.GetProperty("title").GetString());
        Assert.Equal("Completed", g.GetProperty("status").GetString());
        Assert.Equal(50, g.GetProperty("progressPct").GetInt32());
        // Completing stamps an achievement time...
        Assert.NotEqual(JsonValueKind.Null, g.GetProperty("completedAt").ValueKind);

        // ...and moving back to Active clears it.
        var reopen = await client.PutAsJsonAsync($"/api/goals/{id}", new { status = "Active" });
        var g2 = JsonDocument.Parse(await reopen.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Null, g2.GetProperty("completedAt").ValueKind);
    }

    [Fact]
    public async Task List_FiltersByHorizon()
    {
        var client = await SignedInClient("goal-filter@example.com");
        await client.PostAsJsonAsync("/api/goals", new { title = "Yr A", horizon = "Year" });
        await client.PostAsJsonAsync("/api/goals", new { title = "Wk A", horizon = "Week" });

        var weekly = await client.GetAsync("/api/goals?horizon=Week");
        var arr = JsonDocument.Parse(await weekly.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, arr.GetArrayLength());
        Assert.Equal("Wk A", arr[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Goals_AreScopedPerUser()
    {
        var alice = await SignedInClient("alice-goals@example.com");
        var create = await alice.PostAsJsonAsync("/api/goals", new { title = "Private vision", horizon = "Vision25" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var bob = await SignedInClient("bob-goals@example.com");
        var bobList = await bob.GetAsync("/api/goals");
        Assert.Equal(0, JsonDocument.Parse(await bobList.Content.ReadAsStringAsync()).RootElement.GetArrayLength());

        // Bob can neither update nor delete Alice's goal, nor link to it.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/goals/{id}", new { title = "Hijack" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/goals/{id}")).StatusCode);
        var link = await bob.PostAsJsonAsync("/api/goals", new { title = "Mine", horizon = "Year", parentGoalId = id });
        Assert.Equal(HttpStatusCode.BadRequest, link.StatusCode);
    }

    [Fact]
    public async Task Delete_OrphansChildren_RatherThanCascading()
    {
        var client = await SignedInClient("goal-orphan@example.com");
        var parent = await client.PostAsJsonAsync("/api/goals", new { title = "Parent", horizon = "Horizon5" });
        var parentId = JsonDocument.Parse(await parent.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        var child = await client.PostAsJsonAsync("/api/goals", new { title = "Child", horizon = "Year", parentGoalId = parentId });
        var childId = JsonDocument.Parse(await child.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/goals/{parentId}")).StatusCode);

        // Child survives — deleting a parent must not cascade-delete the ladder
        // below it. (The accompanying ParentGoalId -> null is a DB-level SET NULL
        // FK action, enforced by real Postgres but not the in-memory test provider,
        // so it's verified against the migration / live DB rather than asserted here.)
        var list = await client.GetAsync("/api/goals");
        var arr = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, arr.GetArrayLength());
        Assert.Equal(childId, arr[0].GetProperty("id").GetString());
    }
}
