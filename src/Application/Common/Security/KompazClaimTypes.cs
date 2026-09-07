namespace Kompaz.Application.Common.Security;

/// <summary>
/// The claim names carried by an access token. Inbound claim mapping is disabled, so these names are used verbatim.
/// </summary>
public static class KompazClaimTypes
{
	/// <summary>
	/// The user identifier.
	/// </summary>
	public const string Subject = "sub";

	/// <summary>
	/// The email address the user signs in with.
	/// </summary>
	public const string Email = "email";

	/// <summary>
	/// The display name.
	/// </summary>
	public const string Name = "name";

	/// <summary>
	/// The identifier of the organization the user belongs to.
	/// </summary>
	public const string Organization = "org";

	/// <summary>
	/// The <see cref="Kompaz.Domain.Enums.UserRole"/> the user holds.
	/// </summary>
	public const string Role = "role";
}
