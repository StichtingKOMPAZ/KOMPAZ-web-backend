using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kompaz.Infrastructure.Persistence;

/// <summary>
/// Recognizes the provider error behind a violated unique index. Which error that is depends on the provider, so
/// changing provider means changing this file.
/// </summary>
internal static class UniqueConstraint
{
	/// <summary>
	/// PostgreSQL's <c>unique_violation</c>, which covers a duplicate primary key as well as a duplicate value in
	/// any other unique index.
	/// </summary>
	private const string UniqueViolation = "23505";

	/// <summary>
	/// Reports whether a failed save was rejected for duplicating a value, rather than for one of the other reasons
	/// a write can fail. Only uniqueness maps to a conflict; a foreign key, a null, or a full disk do not.
	/// </summary>
	public static bool WasViolated(DbUpdateException exception) =>
		exception.InnerException is PostgresException { SqlState: UniqueViolation };
}
