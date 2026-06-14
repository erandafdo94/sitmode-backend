using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FocusRouter.Api.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "focus-router";
    public string Audience { get; set; } = "focus-router";
    public string Secret { get; set; } = default!;
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
}

public class JwtIssuer
{
    private readonly JwtOptions _opts;

    public JwtIssuer(IOptions<JwtOptions> opts) => _opts = opts.Value;

    /// <summary>Access-token lifetime in seconds, for the client's `expires_in`.</summary>
    public int AccessTokenSeconds => _opts.AccessTokenMinutes * 60;

    public string IssueAccessToken(Guid userId, string email, string? name, string? picture)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
        };
        if (!string.IsNullOrEmpty(name)) claims.Add(new("name", name));
        if (!string.IsNullOrEmpty(picture)) claims.Add(new("picture", picture));

        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
