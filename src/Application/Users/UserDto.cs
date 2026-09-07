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
	DateTimeOffset? LastLoginUtc)
{
	private static readonly Func<User, UserDto> Materialize = Projection.Compile();

	/// <summary>
	/// Gets the server-side projection used by queries so no query fetches more columns than it needs.
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
		user.LastLoginUtc);

	/// <summary>
	/// Maps an entity that is already loaded, including its <see cref="User.Organization"/>.
	/// </summary>
	public static UserDto FromEntity(User user) => Materialize(user);
}
