using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Common.Security;

/// <summary>
/// Tenant boundary checks. A platform administrator reaches every organization; everybody else only their own.
/// </summary>
public static class OrganizationAccess
{
	public static bool IsPlatformAdministrator(IUser user) => user.Role == UserRole.PlatformAdministrator;

	/// <summary>
	/// Throws unless the caller may read the given organization.
	/// </summary>
	public static void EnsureCanRead(IUser user, Guid organizationId)
	{
		if (IsPlatformAdministrator(user) || user.OrganizationId == organizationId)
		{
			return;
		}

		throw new ForbiddenAccessException("The current user does not belong to the requested organization.");
	}

	/// <summary>
	/// Throws unless the caller may change the given organization or the people inside it.
	/// </summary>
	public static void EnsureCanManage(IUser user, Guid organizationId)
	{
		if (IsPlatformAdministrator(user) || (user.OrganizationId == organizationId && user.Role >= UserRole.Administrator))
		{
			return;
		}

		throw new ForbiddenAccessException("The current user is not an administrator of the requested organization.");
	}

	/// <summary>
	/// Throws unless the caller may grant the given role, or act on somebody who already holds it. Only a platform
	/// administrator can create, demote, or remove another platform administrator.
	/// </summary>
	public static void EnsureCanManageRole(IUser user, UserRole role)
	{
		if (role != UserRole.PlatformAdministrator || IsPlatformAdministrator(user))
		{
			return;
		}

		throw new ForbiddenAccessException("Only a platform administrator can manage the platform administrator role.");
	}

	/// <summary>
	/// Throws unless the caller may change somebody's role at all.
	/// <para>
	/// Reserved to platform administrators, which is stricter than <see cref="EnsureCanGrantRole"/> and
	/// deliberately so: an organization administrator runs the people in their organization, but who is an
	/// administrator of it is the platform's call. Inviting is the looser of the two — an administrator may invite
	/// members — because inviting a member creates one rather than moving an existing person between roles.
	/// </para>
	/// </summary>
	public static void EnsureCanChangeRole(IUser user)
	{
		if (IsPlatformAdministrator(user))
		{
			return;
		}

		throw new ForbiddenAccessException("Only a platform administrator can change somebody's role.");
	}

	/// <summary>
	/// Throws unless the caller may hand the given role to somebody else. Managing a role and granting it are not
	/// the same thing: an administrator runs their own organization, which includes removing or renaming a fellow
	/// administrator somebody above them appointed, but not appointing one. So a role is only ever granted from
	/// above, which leaves an administrator able to invite members and nothing more.
	/// </summary>
	public static void EnsureCanGrantRole(IUser user, UserRole role)
	{
		// Keeps the more specific message for the escalation everybody tries first.
		EnsureCanManageRole(user, role);

		if (IsPlatformAdministrator(user) || role < user.Role)
		{
			return;
		}

		throw new ForbiddenAccessException("The current user can only grant a role below their own.");
	}

	/// <summary>
	/// Throws unless the organization may hold the given role. This is not a permission check — the caller is
	/// allowed to grant the role, just not there — so it reports a clash rather than a refusal.
	/// </summary>
	public static void EnsureCanHoldRole(Organization organization, UserRole role)
	{
		if (organization.CanHold(role))
		{
			return;
		}

		throw new ConflictException(
			$"A platform administrator belongs to the organization that runs the platform, not to \"{organization.Name}\".");
	}

	/// <summary>
	/// Resolves the organization a request targets, defaulting to the caller's own organization when none is supplied.
	/// </summary>
	public static Guid ResolveTarget(IUser user, Guid? requestedOrganizationId)
	{
		if (requestedOrganizationId is { } organizationId)
		{
			return organizationId;
		}

		return user.OrganizationId
			?? throw new ForbiddenAccessException("The current user is not attached to an organization.");
	}
}
