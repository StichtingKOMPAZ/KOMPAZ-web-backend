using Kompaz.Domain.Enums;

namespace Kompaz.Domain.Entities;

/// <summary>
/// A single-use credential emailed to a user. Only the hash of the token is stored, never the value from the link.
/// </summary>
public class LoginToken
{
	public Guid Id { get; set; }

	public Guid UserId { get; set; }

	public User User { get; set; } = null!;

	public string TokenHash { get; set; } = string.Empty;

	public LoginTokenPurpose Purpose { get; set; }

	public DateTimeOffset CreatedUtc { get; set; }

	public DateTimeOffset ExpiresUtc { get; set; }

	public DateTimeOffset? ConsumedUtc { get; set; }

	public static LoginToken Issue(Guid userId, string tokenHash, LoginTokenPurpose purpose, DateTimeOffset now, TimeSpan lifetime) =>
		new()
		{
			UserId = userId,
			TokenHash = tokenHash,
			Purpose = purpose,
			CreatedUtc = now,
			ExpiresUtc = now.Add(lifetime),
		};

	public bool IsRedeemable(DateTimeOffset now) => ConsumedUtc is null && ExpiresUtc > now;

	/// <summary>
	/// Marks the token as spent so it cannot be redeemed again.
	/// </summary>
	public void Consume(DateTimeOffset now)
	{
		ConsumedUtc = now;
	}
}
