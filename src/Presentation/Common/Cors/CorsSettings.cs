namespace Kompaz.Presentation.Common.Cors;

/// <summary>
/// Strongly typed cross-origin resource sharing configuration bound from the "Cors" section.
/// </summary>
internal sealed class CorsSettings
{
	public const string SectionName = "Cors";

	/// <summary>
	/// Gets the origins allowed to call the API. When empty, no cross-origin requests are permitted.
	/// </summary>
	public string[] AllowedOrigins { get; init; } = [];
}
