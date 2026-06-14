namespace FocusRouter.Api.Domain;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GoogleSub { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string? PictureUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
