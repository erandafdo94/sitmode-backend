using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace FocusRouter.Api.Auth;

public class GoogleAuthOptions
{
    public string ClientId { get; set; } = default!;
}

public class GoogleTokenVerifier : IGoogleTokenVerifier
{
    private readonly GoogleAuthOptions _opts;

    public GoogleTokenVerifier(IOptions<GoogleAuthOptions> opts) => _opts = opts.Value;

    public async Task<GoogleIdentity> VerifyAsync(string idToken, CancellationToken ct = default)
    {
        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { _opts.ClientId }
        };
        var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
        return new GoogleIdentity(payload.Subject, payload.Email, payload.Name, payload.Picture);
    }
}
