using Kompaz.Domain.Common;

namespace Kompaz.Domain.Events;

/// <summary>
/// An invitation link was issued for a user and needs delivering.
/// <para>
/// Carries the secret from the link, because only the request that issued it ever holds the plaintext — the database
/// keeps a hash. It never reaches a log: the handler passes it to the email sender and nothing else.
/// </para>
/// </summary>
/// <param name="UserId">The user invited.</param>
/// <param name="Email">Where to send it.</param>
/// <param name="Name">Who to greet.</param>
/// <param name="OrganizationName">The organization they were invited to.</param>
/// <param name="Token">The single-use secret the link carries.</param>
public sealed record InvitationIssuedEvent(
	Guid UserId,
	string Email,
	string Name,
	string OrganizationName,
	string Token) : DomainEvent;
