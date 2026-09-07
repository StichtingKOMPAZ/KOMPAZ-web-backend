namespace Kompaz.Application.Common.Search;

/// <summary>
/// Builds SQL <c>LIKE</c> patterns for free-text filters, escaping the wildcards a user may have typed.
/// </summary>
public static class SearchPattern
{
	/// <summary>
	/// The escape character to pass to <c>EF.Functions.Like</c> alongside a pattern from this class.
	/// </summary>
	public const string EscapeCharacter = "\\";

	/// <summary>
	/// Produces a pattern matching any value that contains <paramref name="value"/>.
	/// </summary>
	public static string Contains(string value) =>
		$"%{value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
}
