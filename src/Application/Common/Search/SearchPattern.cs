namespace Kompaz.Application.Common.Search;

/// <summary>
/// Builds SQL <c>LIKE</c> patterns for free-text filters, escaping the wildcards a user may have typed.
/// <para>
/// Patterns are upper-cased, and must be matched against an upper-cased column — <c>candidate.Name.ToUpper()</c>, or
/// a column already stored that way such as <see cref="Domain.Entities.User.NormalizedEmail"/>. How much case
/// <c>LIKE</c> ignores on its own is the database's decision, and PostgreSQL's answer is none, so folding both sides
/// is what makes the documented case-insensitive search mean the same thing wherever this runs.
/// </para>
/// <para>
/// Folding both sides rather than reaching for <c>ILIKE</c> keeps this layer free of any one database's dialect.
/// Both halves fold the same way: PostgreSQL's <c>upper()</c> and .NET's <see cref="string.ToUpperInvariant"/> agree
/// on accented characters, so <c>renée</c> finds <c>Renée</c>.
/// </para>
/// </summary>
public static class SearchPattern
{
	/// <summary>
	/// The escape character to pass to <c>EF.Functions.Like</c> alongside a pattern from this class.
	/// </summary>
	public const string EscapeCharacter = "\\";

	/// <summary>
	/// Produces an upper-cased pattern matching any value that contains <paramref name="value"/>, ignoring case.
	/// </summary>
	public static string Contains(string value) =>
		$"%{Escape(value.ToUpperInvariant())}%";

	/// <summary>
	/// Neutralizes the wildcards a user may have typed, so a search for <c>%</c> looks for a percent sign rather
	/// than for everything. The escape character goes first, or the escapes added after it would be escaped too.
	/// </summary>
	private static string Escape(string value) =>
		value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
