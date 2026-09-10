using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using System.Linq.Expressions;

namespace Kompaz.Application.Organizations;

/// <summary>
/// An organization as exposed over the API, including how its people are split between invited and active.
/// </summary>
public sealed record OrganizationDto(
	Guid Id,
	string Name,
	bool IsPlatform,
	int UserCount,
	int ActiveUserCount,
	int InvitedUserCount,
	DateTimeOffset CreatedUtc,
	DateTimeOffset UpdatedUtc)
{
	/// <summary>
	/// Gets the server-side projection used by queries so the member counts are aggregated by the database.
	/// </summary>
	public static Expression<Func<Organization, OrganizationDto>> Projection => organization => new OrganizationDto(
		organization.Id,
		organization.Name,
		organization.IsPlatform,
		organization.Users.Count,
		organization.Users.Count(user => user.Status == UserStatus.Active),
		organization.Users.Count(user => user.Status == UserStatus.Invited),
		organization.CreatedUtc,
		organization.UpdatedUtc);
}
