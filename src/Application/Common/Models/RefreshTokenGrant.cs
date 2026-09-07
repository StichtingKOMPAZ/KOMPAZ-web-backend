namespace Kompaz.Application.Common.Models;

/// <summary>
/// A refresh token handed to a client, and the moment it stops being accepted if unused.
/// </summary>
public sealed record RefreshTokenGrant(string Value, DateTimeOffset ExpiresUtc);
