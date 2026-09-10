namespace Kompaz.Presentation.Common.RateLimiting;

/// <summary>
/// Strongly typed fixed-window rate limiting configuration bound from the "RateLimiting" section.
/// </summary>
internal sealed class RateLimitSettings
{
	public const string SectionName = "RateLimiting";

	/// <summary>
	/// The policy name applied to the endpoint that sends a sign-in link, so a caller cannot use it to flood an
	/// inbox. It is a bucket of its own rather than a share of <see cref="SignInPolicyName"/>: asking for a second
	/// link and then clicking one are the same login attempt, and spending the redemption budget on the retries
	/// that precede it would refuse the click that the retries were for.
	/// </summary>
	public const string MagicLinkPolicyName = "magic-link";

	/// <summary>
	/// The policy name applied to the token endpoints. These send no email, so the budget is about abuse rather
	/// than about anyone's inbox, and is correspondingly wider.
	/// </summary>
	public const string SignInPolicyName = "sign-in";

	/// <summary>
	/// Gets the maximum number of requests permitted per window, per client IP address.
	/// </summary>
	public int PermitLimit { get; init; } = 100;

	/// <summary>
	/// Gets the length of the fixed window in seconds.
	/// </summary>
	public int WindowSeconds { get; init; } = 60;

	/// <summary>
	/// Gets the number of requests allowed to queue once the permit limit is reached.
	/// </summary>
	public int QueueLimit { get; init; }

	/// <summary>
	/// Gets the maximum number of sign-in emails a single client IP address may trigger per window. Shared by
	/// everyone behind one address, so a value tuned for one person locks out an office; the partition is the
	/// caller's address because that is all this endpoint knows before it has read a body.
	/// </summary>
	public int MagicLinkPermitLimit { get; init; } = 5;

	/// <summary>
	/// Gets the length of the sign-in email fixed window in seconds.
	/// </summary>
	public int MagicLinkWindowSeconds { get; init; } = 300;

	/// <summary>
	/// Gets the maximum number of token exchanges a single client IP address may make per window.
	/// </summary>
	public int SignInPermitLimit { get; init; } = 30;

	/// <summary>
	/// Gets the length of the token fixed window in seconds.
	/// </summary>
	public int SignInWindowSeconds { get; init; } = 300;
}
