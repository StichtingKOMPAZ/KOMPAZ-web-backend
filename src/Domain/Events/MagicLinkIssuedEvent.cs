using Kompaz.Domain.Common;

namespace Kompaz.Domain.Events;

/// <summary>
/// A sign-in link was issued for a user and needs delivering. Carries the secret for the same reason
/// <see cref="InvitationIssuedEvent"/> does.
/// </summary>
/// <param name="UserId">The user signing in.</param>
/// <param name="Email">Where to send it.</param>
/// <param name="Name">Who to greet.</param>
/// <param name="Token">The single-use secret the link carries.</param>
public sealed record MagicLinkIssuedEvent(
	Guid UserId,
	string Email,
	string Name,
	string Token) : DomainEvent;
