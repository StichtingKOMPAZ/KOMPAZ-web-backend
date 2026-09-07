using Kompaz.Domain.Enums;

namespace Kompaz.Application.Common.Interfaces;

/// <summary>
/// The caller behind the current request, read from the access token rather than from the database.
/// </summary>
public interface IUser
{
	Guid? Id { get; }

	Guid? OrganizationId { get; }

	UserRole? Role { get; }
}
