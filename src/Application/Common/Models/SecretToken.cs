namespace Kompaz.Application.Common.Models;

/// <summary>
/// A freshly generated secret. <paramref name="Value"/> goes to the client; only <paramref name="Hash"/> is stored.
/// </summary>
/// <param name="Value">The secret handed out, in a sign-in link or as a refresh token.</param>
/// <param name="Hash">The value persisted alongside the user.</param>
public sealed record SecretToken(string Value, string Hash);
