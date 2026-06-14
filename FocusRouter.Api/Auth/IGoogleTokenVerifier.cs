namespace FocusRouter.Api.Auth;

public record GoogleIdentity(string Sub, string Email, string? Name, string? Picture);

public interface IGoogleTokenVerifier
{
    Task<GoogleIdentity> VerifyAsync(string idToken, CancellationToken ct = default);
}
