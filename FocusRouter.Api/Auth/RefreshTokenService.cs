using System.Security.Cryptography;
using FocusRouter.Api.Data;
using FocusRouter.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FocusRouter.Api.Auth;

// Issues, rotates, and revokes refresh tokens. Raw tokens are 256-bit random
// values returned to the client once; only their SHA-256 hash is stored.
public class RefreshTokenService
{
    private readonly AppDbContext _db;
    private readonly JwtOptions _opts;

    public RefreshTokenService(AppDbContext db, IOptions<JwtOptions> opts)
    {
        _db = db;
        _opts = opts.Value;
    }

    public async Task<string> IssueAsync(Guid userId, CancellationToken ct = default)
    {
        var raw = GenerateRawToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_opts.RefreshTokenDays),
        });
        await _db.SaveChangesAsync(ct);
        return raw;
    }

    // Validates a presented token and, if good, revokes it and issues a successor
    // (rotation). Returns null when the token is unknown, revoked, or expired.
    public async Task<(Guid UserId, string NewRaw)?> RotateAsync(string raw, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var hash = Hash(raw);
        var row = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (row is null || row.RevokedAt is not null || row.ExpiresAt <= DateTimeOffset.UtcNow)
            return null;

        var newRaw = GenerateRawToken();
        var newHash = Hash(newRaw);
        row.RevokedAt = DateTimeOffset.UtcNow;
        row.ReplacedByHash = newHash;
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = row.UserId,
            TokenHash = newHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_opts.RefreshTokenDays),
        });
        await _db.SaveChangesAsync(ct);
        return (row.UserId, newRaw);
    }

    public async Task RevokeAsync(string raw, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        var hash = Hash(raw);
        var row = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (row is not null && row.RevokedAt is null)
        {
            row.RevokedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private static string GenerateRawToken() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
