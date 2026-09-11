using Kompaz.Domain.Common;
using Kompaz.Domain.Enums;

namespace Kompaz.Domain.Entities;

/// <summary>
/// A tenant. Every user belongs to exactly one organization.
/// </summary>
public class Organization : AuditableEntity
{
	/// <summary>
	/// The longest name an organization may have. Read by the validators, by the column, and by the message
	/// that quotes the number, so the three cannot disagree about it.
	/// </summary>
	public const int MaximumNameLength = 200;

	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Gets the upper-cased form of <see cref="Name"/> that carries the unique index, so two organizations cannot
	/// differ by case alone. Kept for the same reason <see cref="User.NormalizedEmail"/> is: the name a person
	/// typed is what they see, and folding it in the index is what makes "uniquely named" mean what it says
	/// however the database collates.
	/// </summary>
	public string NormalizedName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets a value indicating whether this is the organization that runs the platform. Exactly one
	/// organization is, it is planted by the seeder rather than created over the API, and it is the only one whose
	/// people may hold <see cref="UserRole.PlatformAdministrator"/>.
	/// </summary>
	public bool IsPlatform { get; set; }

	/// <summary>
	/// Gets or sets the uploaded logo, or <see langword="null"/> for an organization shown with the placeholder.
	/// <para>
	/// A row of its own rather than a column here, because it holds megabytes: every handler that loads an
	/// organization to rename or delete it would otherwise drag the image across the wire to ignore it.
	/// </para>
	/// </summary>
	public OrganizationLogo? Logo { get; set; }

	public ICollection<User> Users { get; } = [];

	/// <summary>
	/// Normalizes an organization name for storage and lookup.
	/// </summary>
	public static string Normalize(string name) => name.Trim().ToUpperInvariant();

	public static Organization Create(string name) =>
		new()
		{
			Name = name.Trim(),
			NormalizedName = Normalize(name),
		};

	public void Rename(string name)
	{
		Name = name.Trim();
		NormalizedName = Normalize(name);
	}

	/// <summary>
	/// Reports whether somebody in this organization may hold the given role. Platform administration is a job at
	/// the organization that runs the platform, so the role does not travel to a tenant; every other role does.
	/// </summary>
	public bool CanHold(UserRole role) => IsPlatform || role != UserRole.PlatformAdministrator;
}
