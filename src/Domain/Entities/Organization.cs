using Kompaz.Domain.Common;

namespace Kompaz.Domain.Entities;

/// <summary>
/// A tenant. Every user belongs to exactly one organization.
/// </summary>
public class Organization : AuditableEntity
{
	public string Name { get; set; } = string.Empty;

	public ICollection<User> Users { get; } = [];

	public void Rename(string name)
	{
		Name = name;
	}
}
