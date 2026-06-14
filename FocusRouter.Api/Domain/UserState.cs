namespace FocusRouter.Api.Domain;

// The whole client State object, stored verbatim as one jsonb blob per user.
// The frontend is local-first and syncs its entire state in one shot
// (GET/PUT /api/state), so the server treats it as opaque — no per-field schema.
public class UserState
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Raw JSON of the client State; null until the first push.</summary>
    public string? StateJson { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
