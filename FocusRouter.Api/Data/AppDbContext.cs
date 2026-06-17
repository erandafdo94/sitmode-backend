using FocusRouter.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FocusRouter.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserState> UserStates => Set<UserState>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Habit> Habits => Set<Habit>();
    public DbSet<HabitCompletion> HabitCompletions => Set<HabitCompletion>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            // Filtered so multiple email/password users (null GoogleSub) are allowed.
            e.HasIndex(x => x.GoogleSub).IsUnique().HasFilter("\"GoogleSub\" IS NOT NULL");
            // Email is the login identity; kept unique (normalized to lowercase on write).
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<UserState>(e =>
        {
            e.ToTable("user_states");
            e.HasKey(x => x.UserId);
            e.Property(x => x.StateJson).HasColumnType("jsonb");
            e.HasOne(x => x.User).WithOne().HasForeignKey<UserState>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Habit>(e =>
        {
            e.ToTable("habits");
            // Persist the enum as readable text ("Daily"/"Weekly") rather than an int.
            e.Property(x => x.Kind).HasConversion<string>();
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<HabitCompletion>(e =>
        {
            e.ToTable("habit_completions");
            // One check-in per habit per day; makes check-ins idempotent toggles.
            e.HasIndex(x => new { x.HabitId, x.Date }).IsUnique();
            e.HasOne(x => x.Habit).WithMany(h => h.Completions)
                .HasForeignKey(x => x.HabitId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
