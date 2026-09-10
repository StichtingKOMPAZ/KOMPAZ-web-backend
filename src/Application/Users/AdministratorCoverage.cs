using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Users;

/// <summary>
/// The two "somebody has to be left" invariants, in one place because three commands can break them.
/// <para>
/// An organization or a platform with nobody able to administer it cannot fix itself: only an administrator can
/// invite, and only an administrator can appoint one. Deleting, demoting and moving somebody all take a person out
/// of that pool, so all three ask here rather than each remembering the rule for itself.
/// </para>
/// </summary>
public static class AdministratorCoverage
{
	/// <summary>
	/// Throws when taking this person out of an organization's administrators would leave it with none. The caller
	/// decides whether they are leaving it — by deletion, demotion, or a move — and only asks when they are.
	/// </summary>
	public static async Task EnsureAnAdministratorRemainsAsync(
		IApplicationDbContext context,
		Guid organizationId,
		Guid leavingUserId,
		CancellationToken cancellationToken)
	{
		bool anotherRemains = await context.Users
			.AnyAsync(
				candidate => candidate.Id != leavingUserId
					&& candidate.OrganizationId == organizationId
					&& candidate.DeletedUtc == null
					&& candidate.Role >= UserRole.Administrator,
				cancellationToken);

		if (!anotherRemains)
		{
			throw new ConflictException(
				"An organization cannot be left without an administrator. Appoint another one first.");
		}
	}

	/// <summary>
	/// Throws when taking this person out of the platform administrators would leave nobody able to grant the role
	/// back. A deleted one does not count as remaining: they cannot sign in, so they could not appoint anybody.
	/// </summary>
	public static async Task EnsureAPlatformAdministratorRemainsAsync(
		IApplicationDbContext context,
		Guid leavingUserId,
		CancellationToken cancellationToken)
	{
		bool anotherRemains = await context.Users
			.AnyAsync(
				candidate => candidate.Id != leavingUserId
					&& candidate.DeletedUtc == null
					&& candidate.Role == UserRole.PlatformAdministrator,
				cancellationToken);

		if (!anotherRemains)
		{
			throw new ConflictException(
				"The last platform administrator cannot give up the role. Appoint another one first.");
		}
	}
}
