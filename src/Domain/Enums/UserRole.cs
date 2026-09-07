namespace Kompaz.Domain.Enums;

/// <summary>
/// The capabilities a user has. Roles are hierarchical: every higher role includes the rights of the lower ones.
/// </summary>
public enum UserRole
{
	/// <summary>
	/// Can read their own organization and the people in it.
	/// </summary>
	Member = 0,

	/// <summary>
	/// Can invite, update and remove users within their own organization, and update the organization itself.
	/// </summary>
	Administrator = 1,

	/// <summary>
	/// Can manage every organization and every user in the system.
	/// </summary>
	PlatformAdministrator = 2,
}
