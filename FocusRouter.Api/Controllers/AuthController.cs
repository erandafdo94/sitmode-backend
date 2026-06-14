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

        var access = _jwt.IssueAccessToken(user.Id, user.Email, user.DisplayName, user.PictureUrl);
        var refresh = await _refresh.IssueAsync(user.Id, ct);
        var me = new MeDto(user.Id, user.Email, user.DisplayName, user.PictureUrl);
        return Ok(new SignInResponse(access, refresh, _jwt.AccessTokenSeconds, me));
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
