using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using System.Linq.Expressions;

namespace Kompaz.Application.Users;

/// <summary>
/// A user as exposed over the API.
/// </summary>
public sealed record UserDto(
	Guid Id,
	Guid OrganizationId,
	string OrganizationName,
	string Email,
	string Name,
	UserRole Role,
	UserStatus Status,
	DateTimeOffset CreatedUtc,
	DateTimeOffset? InvitedUtc,
	DateTimeOffset? ActivatedUtc,
	DateTimeOffset? LastLoginUtc,
	DateTimeOffset? InvitationExpiresUtc,
	DateTimeOffset? DeletedUtc)
{
	private static readonly Func<User, UserDto> Materialize = Projection.Compile();

	/// <summary>
	/// Gets the server-side projection used by queries so no query fetches more columns than it needs.
	/// <para>
	/// <see cref="InvitationExpiresUtc"/> comes off the outstanding invitation rather than being worked out from
	/// <see cref="InvitedUtc"/> and the configured lifetime: the link's expiry was fixed when it was issued, so
	/// reconfiguring that lifetime cannot retroactively expire or revive one. At most one invitation is outstanding
	/// per user — issuing a new one retires the last — so the aggregate picks that one.
	/// </para>
	/// </summary>
	public static Expression<Func<User, UserDto>> Projection => user => new UserDto(
		user.Id,
		user.OrganizationId,
		user.Organization.Name,
		user.Email,
		user.Name,
		user.Role,
		user.Status,
		user.CreatedUtc,
		user.InvitedUtc,
		user.ActivatedUtc,
		user.LastLoginUtc,
		user.LoginTokens
			.Where(token => token.Purpose == LoginTokenPurpose.Invitation && token.ConsumedUtc == null)
			.Max(token => (DateTimeOffset?)token.ExpiresUtc),
		user.DeletedUtc);

	/// <summary>
	/// Maps an entity that is already loaded, including its <see cref="User.Organization"/>.
	/// <para>
	/// Reports no outstanding invitation rather than reading one off the entity. This serves the authentication
	/// results, where the user has just signed in — so any invitation is spent — and where
	/// <see cref="User.LoginTokens"/> holds whichever tokens the query happened to bring along rather than all of
	/// them, which is not something to answer a question with.
	/// </para>
	/// </summary>
	public static UserDto FromEntity(User user) => Materialize(user) with { InvitationExpiresUtc = null };
}
