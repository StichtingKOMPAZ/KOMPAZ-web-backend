namespace Kompaz.Domain.Enums;

/// <summary>
/// Where a user is in the invitation lifecycle.
/// </summary>
public enum UserStatus
{
	/// <summary>
	/// The user has been invited but has not yet signed in for the first time.
	/// </summary>
	Invited = 0,

	/// <summary>
	/// The user has proven ownership of their email address and can sign in.
	/// </summary>
	Active = 1,
}
