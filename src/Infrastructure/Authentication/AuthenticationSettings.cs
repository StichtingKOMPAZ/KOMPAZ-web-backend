using Kompaz.Application.Common.Interfaces;

namespace Kompaz.Infrastructure.Authentication;

/// <summary>
/// Strongly typed authentication configuration bound from the "Authentication" section.
/// </summary>
internal sealed class AuthenticationSettings : IAuthenticationSettings
{
	public const string SectionName = "Authentication";

	/// <summary>
	/// Gets the issuer stamped on, and required of, every access token.
	/// </summary>
	public string Issuer { get; init; } = string.Empty;

	/// <summary>
	/// Gets the audience stamped on, and required of, every access token.
	/// </summary>
	public string Audience { get; init; } = string.Empty;

	/// <summary>
	/// Gets the symmetric key used to sign access tokens. Must be at least 32 bytes and supplied per environment.
	/// </summary>
	public string SigningKey { get; init; } = string.Empty;

	/// <summary>
	/// Gets how long an issued access token stays valid.
	/// </summary>
	public int AccessTokenLifetimeMinutes { get; init; } = 60;

	/// <summary>
	/// Gets how long a requested sign-in link stays redeemable.
	/// </summary>
	public int MagicLinkLifetimeMinutes { get; init; } = 30;

	/// <summary>
	/// Gets how long an invitation link stays redeemable.
	/// </summary>
	public int InvitationLifetimeDays { get; init; } = 7;

	/// <summary>
	/// Gets the idle window for a refresh token. Every exchange restarts it, so an active client stays signed in.
	/// </summary>
	public int RefreshTokenSlidingLifetimeDays { get; init; } = 14;

	/// <summary>
	/// Gets the ceiling a session reaches however often it is refreshed. Must be at least the sliding window.
	/// </summary>
	public int RefreshTokenAbsoluteLifetimeDays { get; init; } = 90;

	public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenLifetimeMinutes);

	public TimeSpan MagicLinkLifetime => TimeSpan.FromMinutes(MagicLinkLifetimeMinutes);

	public TimeSpan InvitationLifetime => TimeSpan.FromDays(InvitationLifetimeDays);

	public TimeSpan RefreshTokenSlidingLifetime => TimeSpan.FromDays(RefreshTokenSlidingLifetimeDays);

	public TimeSpan RefreshTokenAbsoluteLifetime => TimeSpan.FromDays(RefreshTokenAbsoluteLifetimeDays);

	/// <summary>
	/// Reports the first configuration problem that would make the application unable to issue or accept tokens.
	/// </summary>
	public string? Validate()
	{
		if (string.IsNullOrWhiteSpace(Issuer))
		{
			return $"{SectionName}:{nameof(Issuer)} must be configured.";
		}

		if (string.IsNullOrWhiteSpace(Audience))
		{
			return $"{SectionName}:{nameof(Audience)} must be configured.";
		}

		if (System.Text.Encoding.UTF8.GetByteCount(SigningKey) < 32)
		{
			return $"{SectionName}:{nameof(SigningKey)} must be configured with at least 32 bytes of entropy.";
		}

		if (AccessTokenLifetimeMinutes <= 0
			|| MagicLinkLifetimeMinutes <= 0
			|| InvitationLifetimeDays <= 0
			|| RefreshTokenSlidingLifetimeDays <= 0
			|| RefreshTokenAbsoluteLifetimeDays <= 0)
		{
			return $"{SectionName} lifetimes must all be greater than zero.";
		}

		if (RefreshTokenAbsoluteLifetimeDays < RefreshTokenSlidingLifetimeDays)
		{
			return $"{SectionName}:{nameof(RefreshTokenAbsoluteLifetimeDays)} must be at least "
				+ $"{nameof(RefreshTokenSlidingLifetimeDays)}, otherwise a session expires before its first refresh.";
		}

		return null;
	}
}
