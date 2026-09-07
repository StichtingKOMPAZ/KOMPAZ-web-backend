using Kompaz.Application.Common.Interfaces;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Kompaz.Infrastructure.Email;

/// <summary>
/// Turns a single-use secret into a client link and composes the email that carries it.
/// </summary>
internal sealed class AuthenticationEmailSender : IAuthenticationEmailSender
{
	private readonly EmailSettings _settings;
	private readonly IEmailDispatcher _dispatcher;

	public AuthenticationEmailSender(IOptions<EmailSettings> settings, IEmailDispatcher dispatcher)
	{
		_settings = settings.Value;
		_dispatcher = dispatcher;
	}

	public Task SendMagicLinkAsync(string email, string name, string token, CancellationToken cancellationToken = default)
	{
		string link = BuildLink(_settings.MagicLinkUrl, token);
		string body = string.Create(
			CultureInfo.InvariantCulture,
			$"Hello {name},\n\nUse the link below to sign in. It works once and expires shortly.\n\n{link}\n\nIf you did not request this, you can ignore this email.");

		return _dispatcher.SendAsync(new EmailMessage(email, name, "Your KOMPAZ sign-in link", body), cancellationToken);
	}

	public Task SendInvitationAsync(string email, string name, string organizationName, string token, CancellationToken cancellationToken = default)
	{
		string link = BuildLink(_settings.InvitationUrl, token);
		string body = string.Create(
			CultureInfo.InvariantCulture,
			$"Hello {name},\n\nYou have been invited to join {organizationName} on KOMPAZ.\n\nUse the link below to accept the invitation and sign in.\n\n{link}");

		return _dispatcher.SendAsync(
			new EmailMessage(email, name, $"You have been invited to {organizationName} on KOMPAZ", body),
			cancellationToken);
	}

	private static string BuildLink(string template, string token) =>
		template.Replace(EmailSettings.TokenPlaceholder, Uri.EscapeDataString(token), StringComparison.Ordinal);
}
