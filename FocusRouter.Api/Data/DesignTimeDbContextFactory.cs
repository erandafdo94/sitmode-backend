using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FocusRouter.Api.Data;

// EF Core tooling (dotnet ef migrations add / database update) needs to construct
// an AppDbContext without booting the full app. Program.cs runs MigrateAsync() at
// startup, which would block when Postgres isn't reachable — fall back to a
// design-time factory that just hands EF a configured DbContextOptions.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=focus;Username=postgres;Password=mysecretpassword";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
