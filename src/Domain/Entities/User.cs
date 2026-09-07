using Kompaz.Domain.Enums;

namespace Kompaz.Domain.Entities;

/// <summary>
/// A person who can sign in. Users authenticate with an emailed single-use link rather than a password.
/// </summary>
public class User
{
	public Guid Id { get; set; }

	public Guid OrganizationId { get; set; }

	public Organization Organization { get; set; } = null!;

	public string Email { get; set; } = string.Empty;

	/// <summary>
	/// Gets the upper-cased form of <see cref="Email"/> used for the unique index and for case-insensitive lookups.
	/// </summary>
	public string NormalizedEmail { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public UserRole Role { get; set; }

	public UserStatus Status { get; set; }

	public DateTimeOffset CreatedUtc { get; set; }

	public DateTimeOffset UpdatedUtc { get; set; }

	public DateTimeOffset? InvitedUtc { get; set; }

	public DateTimeOffset? ActivatedUtc { get; set; }

	public DateTimeOffset? LastLoginUtc { get; set; }

	public ICollection<LoginToken> LoginTokens { get; } = [];

	public ICollection<RefreshToken> RefreshTokens { get; } = [];

	/// <summary>
	/// Normalizes an email address for storage and lookup.
	/// </summary>
	public static string Normalize(string email) => email.Trim().ToUpperInvariant();

	public static User Invite(Guid organizationId, string email, string name, UserRole role, DateTimeOffset now) =>
		new()
		{
			OrganizationId = organizationId,
			Email = email.Trim(),
			NormalizedEmail = Normalize(email),
			Name = name.Trim(),
			Role = role,
			Status = UserStatus.Invited,
			CreatedUtc = now,
			UpdatedUtc = now,
			InvitedUtc = now,
		};

	/// <summary>
	/// Records that the user proved ownership of their email address. Activating an already active user is a no-op.
	/// </summary>
	public void Activate(DateTimeOffset now)
	{
		if (Status == UserStatus.Active)
		{
			return;
		}

		Status = UserStatus.Active;
		ActivatedUtc = now;
		UpdatedUtc = now;
	}

	public void RecordLogin(DateTimeOffset now)
	{
		LastLoginUtc = now;
	}

	public void RecordInvitationSent(DateTimeOffset now)
	{
		InvitedUtc = now;
		UpdatedUtc = now;
	}

	/// <summary>
	/// Applies an administrator's edit, which may also move the user between roles.
	/// </summary>
	public void Update(string name, UserRole role, DateTimeOffset now)
	{
		Rename(name, now);
		Role = role;
	}

	/// <summary>
	/// Applies a user's own edit to their profile. The email address is the sign-in identity and the role is not
	/// self-assignable, so neither moves here.
	/// </summary>
	public void Rename(string name, DateTimeOffset now)
	{
		Name = name.Trim();
		UpdatedUtc = now;
	}
}
