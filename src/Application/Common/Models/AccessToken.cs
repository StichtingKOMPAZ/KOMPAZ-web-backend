namespace Kompaz.Application.Common.Models;

/// <summary>
/// A bearer token and the moment it stops being accepted.
/// </summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresUtc);
