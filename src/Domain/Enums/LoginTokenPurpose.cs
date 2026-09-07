namespace Kompaz.Domain.Enums;

/// <summary>
/// Why a single-use login token was issued. Both purposes authenticate; they differ in lifetime and in the email sent.
/// </summary>
public enum LoginTokenPurpose
{
	/// <summary>
	/// A short-lived sign-in link requested by an existing user.
	/// </summary>
	MagicLink = 0,

	/// <summary>
	/// A longer-lived link that both accepts an invitation and signs the user in.
	/// </summary>
	Invitation = 1,
}
