namespace Kompaz.Domain.Entities;

/// <summary>
/// A tenant. Every user belongs to exactly one organization.
/// </summary>
public class Organization
{
	public Guid Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public DateTimeOffset CreatedUtc { get; set; }

	public DateTimeOffset UpdatedUtc { get; set; }

	public ICollection<User> Users { get; } = [];

	public void Rename(string name, DateTimeOffset now)
	{
		Name = name;
		UpdatedUtc = now;
	}
}
