using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace FocusRouter.Api.Auth;

public static class UserIdExtensions
{
    public static Guid UserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(sub, out var id)) return id;
        throw new UnauthorizedAccessException("No user id in token.");
    }
}
