namespace FocusRouter.Api.Domain;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // Null for email/password accounts; set (and unique) for Google accounts.
    public string? GoogleSub { get; set; }
    // Null for Google-only accounts; set for email/password accounts.
    public string? PasswordHash { get; set; }
    public string Email { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string? PictureUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
