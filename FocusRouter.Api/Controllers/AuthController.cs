using FocusRouter.Api.Auth;
using FocusRouter.Api.Data;
using FocusRouter.Api.Domain;
using FocusRouter.Api.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FocusRouter.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IGoogleTokenVerifier _verifier;
    private readonly JwtIssuer _jwt;
    private readonly RefreshTokenService _refresh;
    private readonly AppDbContext _db;

    public AuthController(IGoogleTokenVerifier verifier, JwtIssuer jwt, RefreshTokenService refresh, AppDbContext db)
    {
        _verifier = verifier;
        _jwt = jwt;
        _refresh = refresh;
        _db = db;
    }

    [HttpPost("google")]
    public async Task<IActionResult> Google([FromBody] GoogleSignInRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.IdToken))
            return BadRequest(new { error = "id_token required" });

        GoogleIdentity ident;
        try { ident = await _verifier.VerifyAsync(body.IdToken, ct); }
        catch { return Unauthorized(); }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.GoogleSub == ident.Sub, ct);
        if (user is null)
        {
            user = new User
            {
                GoogleSub = ident.Sub,
                Email = ident.Email,
                DisplayName = ident.Name,
                PictureUrl = ident.Picture,
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            user.Email = ident.Email;
            user.DisplayName = ident.Name;
            user.PictureUrl = ident.Picture;
            await _db.SaveChangesAsync(ct);
        }

        return await IssueSessionAsync(user, ct);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] EmailAuthRequest body, CancellationToken ct)
    {
        var email = (body.Email ?? "").Trim().ToLowerInvariant();
        if (!IsValidEmail(email))
            return BadRequest(new { error = "valid email required" });
        if (string.IsNullOrEmpty(body.Password) || body.Password.Length < 8)
            return BadRequest(new { error = "password must be at least 8 characters" });

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            return Conflict(new { error = "email already in use" });

        var user = new User
        {
            Email = email,
            PasswordHash = PasswordHasher.Hash(body.Password),
            GoogleSub = null,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return await IssueSessionAsync(user, ct);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] EmailAuthRequest body, CancellationToken ct)
    {
        var email = (body.Email ?? "").Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        // Generic failure for unknown email, Google-only accounts, and wrong
        // password alike — avoids leaking which emails exist.
        if (user is null || user.PasswordHash is null || !PasswordHasher.Verify(body.Password ?? "", user.PasswordHash))
            return Unauthorized(new { error = "invalid email or password" });

        return await IssueSessionAsync(user, ct);
    }

    // Issues an access token + refresh token and returns the standard SignInResponse.
    private async Task<IActionResult> IssueSessionAsync(User user, CancellationToken ct)
    {
        var access = _jwt.IssueAccessToken(user.Id, user.Email, user.DisplayName, user.PictureUrl);
        var refresh = await _refresh.IssueAsync(user.Id, ct);
        var me = new MeDto(user.Id, user.Email, user.DisplayName, user.PictureUrl);
        return Ok(new SignInResponse(access, refresh, _jwt.AccessTokenSeconds, me));
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        try { _ = new System.Net.Mail.MailAddress(email); return true; }
        catch (FormatException) { return false; }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest body, CancellationToken ct)
    {
        var rotated = await _refresh.RotateAsync(body.RefreshToken, ct);
        if (rotated is null) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == rotated.Value.UserId, ct);
        if (user is null) return Unauthorized();

        var access = _jwt.IssueAccessToken(user.Id, user.Email, user.DisplayName, user.PictureUrl);
        return Ok(new RefreshResponse(access, rotated.Value.NewRaw, _jwt.AccessTokenSeconds));
    }

    [HttpPost("signout")]
    public async Task<IActionResult> Signout([FromBody] SignoutRequest body, CancellationToken ct)
    {
        await _refresh.RevokeAsync(body.RefreshToken, ct);
        return Ok(new { ok = true });
    }

    [Authorize]
    [HttpGet("/api/me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var uid = User.UserId();
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == uid, ct);
        if (u is null) return NotFound();
        return Ok(new MeDto(u.Id, u.Email, u.DisplayName, u.PictureUrl));
    }
}
