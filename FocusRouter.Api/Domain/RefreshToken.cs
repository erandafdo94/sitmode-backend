namespace FocusRouter.Api.Domain;

// One row per issued refresh token. We store only the SHA-256 hash of the raw
// token (the raw value is returned to the client once and never persisted).
// Rotation: on use, the row is revoked and ReplacedByHash points at its successor.
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = default!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByHash { get; set; }

    public User? User { get; set; }
}
