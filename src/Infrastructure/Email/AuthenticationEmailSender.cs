using Kompaz.Application.Common.Interfaces;
using Kompaz.Infrastructure.Authentication;
using Microsoft.Extensions.Options;

namespace Kompaz.Infrastructure.Email;

/// <summary>
/// Turns a single-use secret into a client link and composes the email that carries it.
/// <para>
/// The wording comes from <see cref="EmailText"/> rather than from string literals here, so a second language is a
/// resource file and not a change to this class. Which language a given recipient gets is the one open question:
/// today every message goes out in <see cref="EmailSettings.DefaultLanguage"/>, because nothing records a person's
/// preference yet. When something does, the two <c>culture</c> locals below are what it feeds.
/// </para>
/// </summary>
internal sealed class AuthenticationEmailSender : IAuthenticationEmailSender
{
	private readonly EmailSettings _settings;
	private readonly AuthenticationSettings _authentication;
	private readonly IEmailDispatcher _dispatcher;

	public AuthenticationEmailSender(
		IOptions<EmailSettings> settings,
		IOptions<AuthenticationSettings> authentication,
		IEmailDispatcher dispatcher)
	{
		_settings = settings.Value;
		_authentication = authentication.Value;
		_dispatcher = dispatcher;
	}

	public Task SendMagicLinkAsync(string email, string name, string token, CancellationToken cancellationToken = default)
	{
		var culture = _settings.Culture;
		string link = BuildLink(_settings.MagicLinkUrl, token);

		// The lifetime is read from configuration rather than written into the wording, so the promise the email
		// makes cannot drift from the deadline the redemption endpoint actually enforces.
		int minutes = (int)_authentication.MagicLinkLifetime.TotalMinutes;

		return _dispatcher.SendAsync(
			new EmailMessage(
				email,
				name,
				EmailText.Get(EmailText.MagicLinkSubject, culture),
				EmailText.Get(EmailText.MagicLinkBody, culture, name, link, minutes)),
			cancellationToken);
	}

	public Task SendInvitationAsync(string email, string name, string organizationName, string token, CancellationToken cancellationToken = default)
	{
		var culture = _settings.Culture;
		string link = BuildLink(_settings.InvitationUrl, token);
		int days = (int)_authentication.InvitationLifetime.TotalDays;

		return _dispatcher.SendAsync(
			new EmailMessage(
				email,
				name,
				EmailText.Get(EmailText.InvitationSubject, culture, organizationName),
				EmailText.Get(EmailText.InvitationBody, culture, name, organizationName, link, days)),
			cancellationToken);
	}

	private static string BuildLink(string template, string token) =>
		template.Replace(EmailSettings.TokenPlaceholder, Uri.EscapeDataString(token), StringComparison.Ordinal);
}
