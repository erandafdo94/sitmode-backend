using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusRouter.Api.Dto;

// Client posts snake_case { id_token }.
public record GoogleSignInRequest([property: JsonPropertyName("id_token")] string IdToken);

// Email/password sign-in (register + login). Client posts { email, password }.
public record EmailAuthRequest(string Email, string Password);

// Set/change the password on the currently authenticated account (e.g. a Google
// user adding a password so they can also log in with email/password).
public record SetPasswordRequest(string Password);

// Sign-in returns an access token (short-lived JWT), a refresh token, the access
// token lifetime in seconds, and the user profile.
public record SignInResponse(
    string Token,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    MeDto User);

// Client reads user.email / user.name / user.picture — map DisplayName/PictureUrl accordingly.
public record MeDto(
    Guid Id,
    string Email,
    [property: JsonPropertyName("name")] string? DisplayName,
    [property: JsonPropertyName("picture")] string? PictureUrl);

public record RefreshRequest([property: JsonPropertyName("refresh_token")] string RefreshToken);
public record RefreshResponse(
    string Token,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public record SignoutRequest([property: JsonPropertyName("refresh_token")] string RefreshToken);

// Whole-state sync envelope: { state: <client State object> }.
public record StatePutRequest(JsonElement State);
