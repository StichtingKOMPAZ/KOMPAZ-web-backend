using Kompaz.Domain.Common;
using Kompaz.Domain.Enums;

namespace Kompaz.Domain.Entities;

/// <summary>
/// A person who can sign in. Users authenticate with an emailed single-use link rather than a password.
/// </summary>
public class User : AuditableEntity
{
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

	public DateTimeOffset? InvitedUtc { get; set; }

	public DateTimeOffset? ActivatedUtc { get; set; }

	public DateTimeOffset? LastLoginUtc { get; set; }

	/// <summary>
	/// Gets when an administrator deleted this user, or <see langword="null"/> for a user who is not deleted.
	/// <para>
	/// Deletion is a soft one, and deliberately separate from <see cref="Status"/> rather than a value of it: the
	/// status records where somebody is in the invitation lifecycle, and overwriting it would lose the answer that
	/// restoring them has to put back. It also keeps the row, which is what the logs and the audit trail on every
	/// other table point at — <c>CreatedBy</c> and <c>UpdatedBy</c> hold identifiers, so a row removed for real
	/// would leave those unresolvable.
	/// </para>
	/// </summary>
	public DateTimeOffset? DeletedUtc { get; set; }

	/// <summary>
	/// Gets a value indicating whether an administrator has deleted this user.
	/// </summary>
	public bool IsDeleted => DeletedUtc is not null;

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
	}

	public void RecordLogin(DateTimeOffset now)
	{
		LastLoginUtc = now;
	}

	public void RecordInvitationSent(DateTimeOffset now)
	{
		InvitedUtc = now;
	}

	/// <summary>
	/// Marks the user as deleted. Their credentials are not this object's to remove; the handler deletes those
	/// outright, because a deleted user must not be able to sign in even if they are restored later.
	/// </summary>
	public void Delete(DateTimeOffset now)
	{
		DeletedUtc = now;
	}

	/// <summary>
	/// Undoes a deletion, returning the user to exactly the lifecycle stage they were at. It does not give them a
	/// way back in: their sign-in links and sessions were deleted, so they start again from the login page.
	/// </summary>
	public void Restore()
	{
		DeletedUtc = null;
	}

	/// <summary>
	/// Brings a deleted user back as a fresh invitation, under whatever name and role the new invitation names.
	/// <para>
	/// This is what stops deleting somebody from burning their email address for good. The address is the unique
	/// key and the row outlives the deletion, so inviting it again has to reuse that row — which also keeps every
	/// log and audit entry pointing at the same person rather than splitting them across two identifiers.
	/// <see cref="ActivatedUtc"/> and <see cref="LastLoginUtc"/> are left alone on purpose: they record what did
	/// happen, and this invitation has not been accepted yet.
	/// </para>
	/// </summary>
	public void ReviveAsInvited(string name, UserRole role, DateTimeOffset now)
	{
		Restore();
		Update(name, role);
		Status = UserStatus.Invited;
		InvitedUtc = now;
	}

	/// <summary>
	/// Points the account at a different address.
	/// <para>
	/// This is the sign-in identity, not a contact detail, so whoever holds the new inbox can sign in as this
	/// person from now on. Nothing here proves they asked for it — an administrator is trusted to have checked —
	/// which is why the caller also retires any link already sent to the old address.
	/// </para>
	/// </summary>
	public void ChangeEmail(string email)
	{
		Email = email.Trim();
		NormalizedEmail = Normalize(email);
	}

	/// <summary>
	/// Moves the user into another organization, which costs them their role.
	/// <para>
	/// A role is held within an organization and says nothing about the next one, so carrying it across would hand
	/// somebody rights over people who never appointed them. They arrive as a member and are promoted there if the
	/// new organization wants that.
	/// </para>
	/// </summary>
	public void MoveTo(Guid organizationId)
	{
		OrganizationId = organizationId;
		Role = UserRole.Member;
	}

	/// <summary>
	/// Applies an administrator's edit, which may also move the user between roles.
	/// </summary>
	public void Update(string name, UserRole role)
	{
		Rename(name);
		Role = role;
	}

	/// <summary>
	/// Applies a user's own edit to their profile. The email address is the sign-in identity and the role is not
	/// self-assignable, so neither moves here.
	/// </summary>
	public void Rename(string name)
	{
		Name = name.Trim();
	}
}
