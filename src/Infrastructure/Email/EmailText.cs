using System.Globalization;
using System.Resources;

namespace Kompaz.Infrastructure.Email;

/// <summary>
/// The wording of the outbound emails, looked up per call in the recipient's language.
/// <para>
/// The culture is an argument rather than <see cref="CultureInfo.CurrentUICulture"/> on purpose. Ambient culture is
/// per-thread state, and these emails are composed on a request thread whose culture belongs to whoever made the
/// request — which, for an invitation, is the administrator and not the recipient. Passing it makes the language a
/// property of the message being written.
/// </para>
/// <para>
/// Dutch lives in <c>EmailResources.resx</c> and is the fallback for any culture that has no translation; English
/// lives in <c>EmailResources.en.resx</c>. Adding a language means adding one file, nothing here.
/// </para>
/// </summary>
internal static class EmailText
{
	public const string MagicLinkSubject = nameof(MagicLinkSubject);

	public const string MagicLinkBody = nameof(MagicLinkBody);

	public const string InvitationSubject = nameof(InvitationSubject);

	public const string InvitationBody = nameof(InvitationBody);

	public const string AccountDeletedSubject = nameof(AccountDeletedSubject);

	public const string AccountDeletedBody = nameof(AccountDeletedBody);

	private static readonly ResourceManager Resources =
		new("Kompaz.Infrastructure.Email.EmailResources", typeof(EmailText).Assembly);

	/// <summary>
	/// Returns one piece of wording in the culture given, with its placeholders filled in.
	/// </summary>
	/// <param name="key">Which piece of wording; one of the constants on this type.</param>
	/// <param name="culture">The language to write in. Falls back to Dutch when it has no translation.</param>
	/// <param name="arguments">The values for the placeholders, in the order the resource comment records.</param>
	public static string Get(string key, CultureInfo culture, params object[] arguments)
	{
		// A missing resource is a deployment that shipped without its satellite assembly, or a key that was renamed
		// in one file and not the other. Either way the recipient gets an email, and an operator gets a subject line
		// that names the problem, rather than a NullReferenceException swallowed by the event handler.
		string? format = Resources.GetString(key, culture);

		if (format is null)
		{
			return key;
		}

		return arguments.Length == 0
			? format
			: string.Format(culture, format, arguments);
	}
}
