namespace Kompaz.Domain.Common;

/// <summary>
/// An entity that records when it was created and last changed, and by whom.
/// <para>
/// Nothing sets these by hand. They are stamped while saving, so a new write cannot forget them and two writes
/// cannot disagree about what time it is. The timestamps are always stamped; the two identifiers are only stamped
/// when a request is behind the change, because the seeder and the sign-in flow both write without a caller.
/// </para>
/// </summary>
public abstract class AuditableEntity : Entity
{
	public DateTimeOffset CreatedUtc { get; set; }

	/// <summary>
	/// Gets the user who created this, or <see langword="null"/> when nobody was signed in — the seeded organization
	/// and the first administrator, for instance.
	/// </summary>
	public Guid? CreatedBy { get; set; }

	public DateTimeOffset UpdatedUtc { get; set; }

	/// <summary>
	/// Gets the user who last changed this, or <see langword="null"/> when the change had no caller behind it, such
	/// as the activation that redeeming a sign-in link performs.
	/// </summary>
	public Guid? UpdatedBy { get; set; }
}
