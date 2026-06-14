using System.Text.Json;
using System.Text.Json.Nodes;
using FocusRouter.Api.Auth;
using FocusRouter.Api.Data;
using FocusRouter.Api.Domain;
using FocusRouter.Api.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FocusRouter.Api.Controllers;

// Whole-state sync for the local-first client. When signed in, the client pulls +
// merges on sign-in (GET) and pushes its entire state on every change (PUT, debounced).
[ApiController]
[Route("api/state")]
[Authorize]
public class StateController : ControllerBase
{
    private readonly AppDbContext _db;

    public StateController(AppDbContext db) => _db = db;

    // GET /api/state -> { state: <object|null> }
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var uid = User.UserId();
        var row = await _db.UserStates.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == uid, ct);
        JsonNode? state = string.IsNullOrWhiteSpace(row?.StateJson) ? null : JsonNode.Parse(row!.StateJson);
        return Ok(new { state });
    }

    // PUT /api/state  body: { state: <object> } -> { ok, updatedAt }
    [HttpPut]
    public async Task<IActionResult> Put([FromBody] StatePutRequest body, CancellationToken ct)
    {
        var uid = User.UserId();
        // Persist the state object verbatim; treat absent/null as "clear".
        var json = body.State.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? null
            : body.State.GetRawText();

        var row = await _db.UserStates.FirstOrDefaultAsync(s => s.UserId == uid, ct);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            _db.UserStates.Add(new UserState { UserId = uid, StateJson = json, UpdatedAt = now });
        }
        else
        {
            row.StateJson = json;
            row.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true, updatedAt = now });
    }
}
