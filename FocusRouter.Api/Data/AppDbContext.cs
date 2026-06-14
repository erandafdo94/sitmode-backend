using FocusRouter.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FocusRouter.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserState> UserStates => Set<UserState>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(x => x.GoogleSub).IsUnique();
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
    }
}
