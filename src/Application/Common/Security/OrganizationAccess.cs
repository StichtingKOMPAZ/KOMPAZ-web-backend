using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
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
