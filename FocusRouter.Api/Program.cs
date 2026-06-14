using System.Text;
using FocusRouter.Api.Auth;
using FocusRouter.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection("Google"));

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
          ?? throw new InvalidOperationException("Jwt config missing");

// Connection string resolution mirrors breadwatch: prefer Railway's DATABASE_URL
// (postgresql://user:pass@host:port/db) and parse it into Npgsql key=value form
// with SSL Mode=Require; fall back to ConnectionStrings:Default for local dev.
var connectionString = ResolveConnectionString(builder.Configuration);
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

static string ResolveConnectionString(IConfiguration config)
{
    var url = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        return
            $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={uri.AbsolutePath.Trim('/')};" +
            $"Username={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo.Length > 1 ? userInfo[1] : "")};" +
            $"SSL Mode=Require;Trust Server Certificate=true";
    }
    return config.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set DATABASE_URL or ConnectionStrings:Default");
}

builder.Services.AddSingleton<JwtIssuer>();
builder.Services.AddSingleton<IGoogleTokenVerifier, GoogleTokenVerifier>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddControllers();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

var allowedOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Auto-migrate on startup so a fresh Railway deploy (or fresh local Postgres)
// stands the schema up without a separate `dotnet ef database update`.
// Skipped under the Testing environment, where tests supply their own provider.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { ok = true }));

app.MapControllers();

app.Run();

public partial class Program { }
