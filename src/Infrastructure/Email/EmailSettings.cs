using System.Globalization;

namespace Kompaz.Infrastructure.Email;

/// <summary>
/// Strongly typed outbound email configuration bound from the "Email" section.
/// </summary>
internal sealed class EmailSettings
{
	public const string SectionName = "Email";

	/// <summary>
	/// The placeholder replaced with the single-use secret when building a sign-in link.
	/// </summary>
	public const string TokenPlaceholder = "{token}";

	/// <summary>
	/// Gets the address sign-in emails are sent from.
	/// </summary>
	public string FromAddress { get; init; } = string.Empty;

	/// <summary>
	/// Gets the display name sign-in emails are sent from.
	/// </summary>
	public string FromName { get; init; } = "KOMPAZ";

	/// <summary>
	/// Gets the language outbound email is written in, as a culture name such as <c>nl</c> or <c>en</c>. Dutch is
	/// the product's language and the neutral resource, so an unrecognized value still produces a Dutch email
	/// rather than no email; <see cref="Validate"/> refuses one that names no culture at all.
	/// </summary>
	public string DefaultLanguage { get; init; } = "nl";

	/// <summary>
	/// Gets <see cref="DefaultLanguage"/> as a culture. Resolved once per read from the framework's cache.
	/// </summary>
	public CultureInfo Culture => TryResolveCulture(DefaultLanguage) ?? CultureInfo.InvariantCulture;

	/// <summary>
	/// Gets the client URL for a sign-in link, containing <see cref="TokenPlaceholder"/>.
	/// </summary>
	public string MagicLinkUrl { get; init; } = string.Empty;

	/// <summary>
	/// Gets the client URL for an invitation link, containing <see cref="TokenPlaceholder"/>.
	/// </summary>
	public string InvitationUrl { get; init; } = string.Empty;

	/// <summary>
	/// Gets the SMTP relay to deliver through. Until it is fully configured, emails are written to the log instead,
	/// so the API stays usable before anyone has set up a mailbox.
	/// </summary>
	public SmtpSettings Smtp { get; init; } = new();

	/// <summary>
	/// Reports the first configuration problem that would stop sign-in emails from being delivered.
	/// </summary>
	public string? Validate()
	{
		if (string.IsNullOrWhiteSpace(FromAddress))
		{
			return $"{SectionName}:{nameof(FromAddress)} must be configured.";
		}

		if (!MagicLinkUrl.Contains(TokenPlaceholder, StringComparison.Ordinal))
		{
			return $"{SectionName}:{nameof(MagicLinkUrl)} must contain the {TokenPlaceholder} placeholder.";
		}

		if (!InvitationUrl.Contains(TokenPlaceholder, StringComparison.Ordinal))
		{
			return $"{SectionName}:{nameof(InvitationUrl)} must contain the {TokenPlaceholder} placeholder.";
		}

		if (TryResolveCulture(DefaultLanguage) is not { } culture || !IsKnownCulture(culture))
		{
			return $"{SectionName}:{nameof(DefaultLanguage)} must be a culture name such as \"nl\" or \"en\" "
				+ $"(current value: \"{DefaultLanguage}\").";
		}

		return null;
	}

	/// <summary>
	/// Returns the culture a name refers to, or null when the name is not one at all. Caught rather than probed,
	/// because the framework offers no non-throwing lookup, and the framework's own cache makes the happy path
	/// cheap enough to call per message.
	/// </summary>
	private static CultureInfo? TryResolveCulture(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		try
		{
			return CultureInfo.GetCultureInfo(name);
		}
		catch (CultureNotFoundException)
		{
			return null;
		}
	}

	/// <summary>
	/// Whether this is a culture the framework knows, rather than one it invented from a name that merely looked
	/// like a language tag — which <see cref="CultureInfo.GetCultureInfo(string)"/> does without complaining. A
	/// typo would otherwise pass startup and then quietly send every email in the neutral language, so the one
	/// symptom of a misconfigured deployment would be Dutch email that was supposed to be something else. Walking
	/// the full list is affordable because <see cref="Validate"/> runs once, at startup.
	/// </summary>
	private static bool IsKnownCulture(CultureInfo culture) =>
		Array.Exists(
			CultureInfo.GetCultures(CultureTypes.AllCultures),
			known => string.Equals(known.Name, culture.Name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Connection details for the SMTP relay. In development this points at a Mailtrap sandbox inbox, which captures
/// mail instead of delivering it, so invitations and sign-in links can be read without reaching a real recipient.
/// </summary>
internal sealed class SmtpSettings
{
	/// <summary>
	/// The marker left in checked-in settings where a secret belongs. A value still reading this has not been
	/// supplied, and counts as absent rather than as a credential.
	/// </summary>
	public const string UnsetPlaceholder = "<set-with-user-secrets>";

	public string Host { get; init; } = string.Empty;

	public int Port { get; init; } = 2525;

	public bool UseStartTls { get; init; } = true;

	public string UserName { get; init; } = string.Empty;

	public string Password { get; init; } = string.Empty;

	/// <summary>
	/// Gets a value indicating whether there is enough here to actually reach a relay. Mailtrap, like every hosted
	/// relay, rejects unauthenticated sessions, so a host without credentials would fail on the first send. Treating
	/// that as "not configured" keeps a fresh checkout working: mail goes to the log until the secrets are supplied.
	/// </summary>
	public bool IsConfigured => IsSupplied(Host) && IsSupplied(UserName) && IsSupplied(Password);

	private static bool IsSupplied(string value) =>
		!string.IsNullOrWhiteSpace(value) && !string.Equals(value, UnsetPlaceholder, StringComparison.Ordinal);
}
