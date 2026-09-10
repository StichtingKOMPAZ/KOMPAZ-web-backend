using Kompaz.Domain.Common;
using Kompaz.Domain.Enums;

namespace Kompaz.Domain.Entities;

/// <summary>
/// A tenant. Every user belongs to exactly one organization.
/// </summary>
public class Organization : AuditableEntity
{
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets a value indicating whether this is the organization that runs the platform. Exactly one
	/// organization is, it is planted by the seeder rather than created over the API, and it is the only one whose
	/// people may hold <see cref="UserRole.PlatformAdministrator"/>.
	/// </summary>
	public bool IsPlatform { get; set; }

	public ICollection<User> Users { get; } = [];

	public void Rename(string name)
	{
		Name = name;
	}

	/// <summary>
	/// Reports whether somebody in this organization may hold the given role. Platform administration is a job at
	/// the organization that runs the platform, so the role does not travel to a tenant; every other role does.
	/// </summary>
	public bool CanHold(UserRole role) => IsPlatform || role != UserRole.PlatformAdministrator;
}
