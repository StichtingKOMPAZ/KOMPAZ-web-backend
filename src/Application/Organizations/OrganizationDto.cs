using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using System.Globalization;
using System.Linq.Expressions;

namespace Kompaz.Application.Organizations;

/// <summary>
/// An organization as exposed over the API, including how its people are split between invited and active.
/// </summary>
public sealed record OrganizationDto(
	Guid Id,
	string Name,
	bool IsPlatform,
	bool HasLogo,
	int UserCount,
	int ActiveUserCount,
	int InvitedUserCount,
	DateTimeOffset CreatedUtc,
	DateTimeOffset UpdatedUtc)
{
	/// <summary>
	/// Gets the server-side projection used by queries so the member counts are aggregated by the database.
	/// <para>
	/// All three counts leave deleted users out, stated here the way every other query over <c>Users</c> states
	/// it. They have to: the roster behind "Gebruikers (n)" leaves them out, and a count that included them would
	/// promise a row of people the list it expands into does not name.
	/// </para>
	/// </summary>
	public static Expression<Func<Organization, OrganizationDto>> Projection => organization => new OrganizationDto(
		organization.Id,
		organization.Name,
		organization.IsPlatform,
		organization.Logo != null,
		organization.Users.Count(user => user.DeletedUtc == null),
		organization.Users.Count(user => user.DeletedUtc == null && user.Status == UserStatus.Active),
		organization.Users.Count(user => user.DeletedUtc == null && user.Status == UserStatus.Invited),
		organization.CreatedUtc,
		organization.UpdatedUtc);

	/// <summary>
	/// Gets where to fetch this organization's logo. Always populated, and always answers: an organization with no
	/// uploaded logo is served the placeholder, so a client has one address to point at rather than a branch and a
	/// copy of the fallback image. <see cref="HasLogo"/> is for a client that needs to tell the two apart, such as
	/// one offering to replace an image that is really there.
	/// </summary>
	public string LogoUrl => string.Create(CultureInfo.InvariantCulture, $"/api/organizations/{Id}/logo");
}
