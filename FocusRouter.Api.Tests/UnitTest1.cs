using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FocusRouter.Api.Auth;
using FocusRouter.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FocusRouter.Api.Tests;

// Stub Google verifier: accepts any id_token and returns a fixed identity, so the
// auth lifecycle can be exercised without a real Google round-trip.
internal sealed class StubGoogleVerifier : IGoogleTokenVerifier
{
    public Task<GoogleIdentity> VerifyAsync(string idToken, CancellationToken ct = default) =>
        Task.FromResult(new GoogleIdentity("google-sub-123", "test@example.com", "Test User", null));
}

public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Swap Npgsql for an isolated in-memory store. EF Core 10 applies the
            // provider via IDbContextOptionsConfiguration<T>, so that must be removed
            // too — otherwise both Npgsql and InMemory providers get registered.
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

            // Swap the real Google verifier for the stub.
            services.RemoveAll<IGoogleTokenVerifier>();
            services.AddSingleton<IGoogleTokenVerifier, StubGoogleVerifier>();
        });
    }
}

public class AuthLifecycleTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public AuthLifecycleTests(TestAppFactory factory) => _factory = factory;

    private static void Bearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task FullLifecycle_SignIn_State_Refresh_Rotation_Signout()
    {
        var client = _factory.CreateClient();

        // 1. Sign in with Google -> access token + refresh token.
        var signIn = await client.PostAsJsonAsync("/api/auth/google", new { id_token = "anything" });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        var signInDoc = JsonDocument.Parse(await signIn.Content.ReadAsStringAsync());
        var accessToken = signInDoc.RootElement.GetProperty("token").GetString()!;
        var refreshToken = signInDoc.RootElement.GetProperty("refresh_token").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));

        // 2. PUT /api/state with the access token -> 200.
        Bearer(client, accessToken);
        var put = await client.PutAsJsonAsync("/api/state", new { state = new { tasks = Array.Empty<object>() } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // GET it back to confirm persistence.
        var get = await client.GetAsync("/api/state");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var getDoc = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.True(getDoc.RootElement.GetProperty("state").TryGetProperty("tasks", out _));

        // 3. Refresh -> new access + new refresh token (rotation).
        client.DefaultRequestHeaders.Authorization = null;
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refresh_token = refreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshDoc = JsonDocument.Parse(await refresh.Content.ReadAsStringAsync());
        var newAccess = refreshDoc.RootElement.GetProperty("token").GetString()!;
        var newRefresh = refreshDoc.RootElement.GetProperty("refresh_token").GetString()!;
        Assert.NotEqual(refreshToken, newRefresh);

        // The new access token still works against a protected route.
        Bearer(client, newAccess);
        var meOk = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, meOk.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        // 4. The OLD refresh token is now revoked -> 401 (rotation enforced).
        var reuseOld = await client.PostAsJsonAsync("/api/auth/refresh", new { refresh_token = refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseOld.StatusCode);

        // 5. Sign out revokes the current refresh token.
        var signout = await client.PostAsJsonAsync("/api/auth/signout", new { refresh_token = newRefresh });
        Assert.Equal(HttpStatusCode.OK, signout.StatusCode);

        // 6. The signed-out refresh token no longer works.
        var afterSignout = await client.PostAsJsonAsync("/api/auth/refresh", new { refresh_token = newRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, afterSignout.StatusCode);
    }

    [Fact]
    public async Task State_RequiresAuth()
    {
        var client = _factory.CreateClient();
        var get = await client.GetAsync("/api/state");
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
    }
}

public class EmailAuthTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public EmailAuthTests(TestAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_ReturnsTokens_AndProtectsState()
    {
        var client = _factory.CreateClient();

        var reg = await client.PostAsJsonAsync("/api/auth/register", new { email = "alice@example.com", password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var doc = JsonDocument.Parse(await reg.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("token").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("refresh_token").GetString()));
        Assert.Equal("alice@example.com", doc.RootElement.GetProperty("user").GetProperty("email").GetString());

        // The issued access token works against a protected route.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Conflicts()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register", new { email = "dup@example.com", password = "secret123" });
        var again = await client.PostAsJsonAsync("/api/auth/register", new { email = "dup@example.com", password = "secret123" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_BadRequest()
    {
        var client = _factory.CreateClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { email = "short@example.com", password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, reg.StatusCode);
    }

    [Fact]
    public async Task Login_GoodAndBadPassword()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register", new { email = "bob@example.com", password = "secret123" });

        // Email is matched case-insensitively (normalized to lowercase on write).
        var good = await client.PostAsJsonAsync("/api/auth/login", new { email = "BOB@example.com", password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);

        var bad = await client.PostAsJsonAsync("/api/auth/login", new { email = "bob@example.com", password = "wrongpass" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = "secret123" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task Login_AgainstGoogleOnlyAccount_Unauthorized()
    {
        var client = _factory.CreateClient();
        // Stub Google verifier creates user test@example.com with no password.
        await client.PostAsJsonAsync("/api/auth/google", new { id_token = "anything" });

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "test@example.com", password = "secret123" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task SetPassword_OnGoogleAccount_ThenEmailLoginWorks()
    {
        var client = _factory.CreateClient();

        // Sign in with Google (creates test@example.com, no password).
        var g = await client.PostAsJsonAsync("/api/auth/google", new { id_token = "anything" });
        var token = JsonDocument.Parse(await g.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;

        // set-password requires auth.
        var unauth = await client.PostAsJsonAsync("/api/auth/set-password", new { password = "newpass123" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

        // Too-short password is rejected.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var tooShort = await client.PostAsJsonAsync("/api/auth/set-password", new { password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        // Set a valid password on the Google account.
        var set = await client.PostAsJsonAsync("/api/auth/set-password", new { password = "newpass123" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        // Now email/password login with the Google email works.
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "test@example.com", password = "newpass123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
