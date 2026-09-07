using Kompaz.Application.Users;

namespace Kompaz.Application.Authentication;

/// <summary>
/// A signed-in session: a short-lived bearer token, the refresh token that renews it, and the profile they belong to.
/// </summary>
public sealed record AuthenticationResultDto(
	string AccessToken,
	string TokenType,
	DateTimeOffset ExpiresUtc,
	string RefreshToken,
	DateTimeOffset RefreshTokenExpiresUtc,
	UserDto User);
