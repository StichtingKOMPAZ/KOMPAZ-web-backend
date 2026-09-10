using Kompaz.Domain.Common;

namespace Kompaz.Domain.Events;

/// <summary>
/// An administrator deleted a user, who needs telling. Carries the address and the name as values rather than an
/// identifier to load: the reaction runs after the save, by which time the roster no longer lists this person, and
/// a notification that had to look them up would be reading a row the rest of the system now treats as gone.
/// </summary>
/// <param name="UserId">The user who was deleted.</param>
/// <param name="Email">Where to send the notice.</param>
/// <param name="Name">Who to greet.</param>
public sealed record UserDeletedEvent(
	Guid UserId,
	string Email,
	string Name) : DomainEvent;
