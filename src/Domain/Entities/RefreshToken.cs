using Kompaz.Domain.Common;

namespace Kompaz.Domain.Entities;

/// <summary>
/// A long-lived credential that buys a new access token without another trip through the inbox.
/// <para>
/// Expiry slides: every use issues a successor whose window restarts from that moment, so an active client stays
/// signed in indefinitely while an idle one lapses. <see cref="AbsoluteExpiresUtc"/> is the ceiling the slide can
/// never pass, so a session still has a hard end.
/// </para>
/// <para>
/// Tokens rotate on every use. A successor keeps the <see cref="SessionId"/> of the token it replaced, which is what
/// lets a replayed token revoke the whole chain rather than just itself.
/// </para>
/// </summary>
public class RefreshToken : Entity
{
	public Guid UserId { get; set; }

	public User User { get; set; } = null!;

	/// <summary>
	/// Gets the rotation chain this token belongs to. Shared by every successor of the sign-in that started it.
	/// </summary>
	public Guid SessionId { get; set; }

	public string TokenHash { get; set; } = string.Empty;

	public DateTimeOffset CreatedUtc { get; set; }

	/// <summary>
	/// Gets the sliding deadline, restarted on every rotation and capped by <see cref="AbsoluteExpiresUtc"/>.
	/// </summary>
	public DateTimeOffset ExpiresUtc { get; set; }

	/// <summary>
	/// Gets the moment the session ends no matter how often it is refreshed.
	/// </summary>
	public DateTimeOffset AbsoluteExpiresUtc { get; set; }

	/// <summary>
	/// Gets the moment this token was exchanged for a successor.
	/// </summary>
	public DateTimeOffset? ConsumedUtc { get; set; }

	/// <summary>
	/// Gets the moment this token was withdrawn, by signing out or by the replay of a sibling in the same session.
	/// </summary>
	public DateTimeOffset? RevokedUtc { get; set; }

	/// <summary>
	/// Gets a value indicating whether the token has already been spent or withdrawn. Presenting one of these is a
	/// replay: the secret leaked, or a client is retrying, and either way the session can no longer be trusted.
	/// </summary>
	public bool IsSpent => ConsumedUtc is not null || RevokedUtc is not null;

	public static RefreshToken StartSession(
		Guid userId,
		string tokenHash,
		DateTimeOffset now,
		TimeSpan slidingLifetime,
		TimeSpan absoluteLifetime)
	{
		var absoluteExpiresUtc = now.Add(absoluteLifetime);

		return new RefreshToken
		{
			UserId = userId,
			SessionId = Guid.NewGuid(),
			TokenHash = tokenHash,
			CreatedUtc = now,
			ExpiresUtc = Earliest(now.Add(slidingLifetime), absoluteExpiresUtc),
			AbsoluteExpiresUtc = absoluteExpiresUtc,
		};
	}

	public bool IsRedeemable(DateTimeOffset now) => !IsSpent && ExpiresUtc > now;

	/// <summary>
	/// Spends this token and returns its successor in the same session, with the sliding window restarted.
	/// </summary>
	public RefreshToken Rotate(string successorHash, DateTimeOffset now, TimeSpan slidingLifetime)
	{
		Consume(now);

		return new RefreshToken
		{
			UserId = UserId,
			SessionId = SessionId,
			TokenHash = successorHash,
			CreatedUtc = now,
			ExpiresUtc = Earliest(now.Add(slidingLifetime), AbsoluteExpiresUtc),
			AbsoluteExpiresUtc = AbsoluteExpiresUtc,
		};
	}

	public void Consume(DateTimeOffset now)
	{
		ConsumedUtc ??= now;
	}

	public void Revoke(DateTimeOffset now)
	{
		RevokedUtc ??= now;
	}

	private static DateTimeOffset Earliest(DateTimeOffset first, DateTimeOffset second) =>
		first < second ? first : second;
}
