namespace Kompaz.Application.Common.Interfaces;

/// <summary>
/// The lifetimes that use cases need when issuing sign-in links and refresh tokens. Bound from configuration in the
/// infrastructure layer.
/// </summary>
public interface IAuthenticationSettings
{
	TimeSpan MagicLinkLifetime { get; }

	TimeSpan InvitationLifetime { get; }

	/// <summary>
	/// Gets the window a refresh token stays usable, restarted every time one is exchanged.
	/// </summary>
	TimeSpan RefreshTokenSlidingLifetime { get; }

	/// <summary>
	/// Gets the ceiling a session can reach however often it is refreshed.
	/// </summary>
	TimeSpan RefreshTokenAbsoluteLifetime { get; }
}
