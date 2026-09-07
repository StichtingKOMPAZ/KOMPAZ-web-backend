namespace Kompaz.Presentation.Common.RateLimiting;

/// <summary>
/// Strongly typed fixed-window rate limiting configuration bound from the "RateLimiting" section.
/// </summary>
internal sealed class RateLimitSettings
{
	public const string SectionName = "RateLimiting";

	/// <summary>
	/// The policy name applied to the endpoints that send email, so a caller cannot use them to flood an inbox.
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
	/// Gets the maximum number of sign-in emails a single client IP address may trigger per window.
	/// </summary>
	public int SignInPermitLimit { get; init; } = 5;

	/// <summary>
	/// Gets the length of the sign-in fixed window in seconds.
	/// </summary>
	public int SignInWindowSeconds { get; init; } = 300;
}
