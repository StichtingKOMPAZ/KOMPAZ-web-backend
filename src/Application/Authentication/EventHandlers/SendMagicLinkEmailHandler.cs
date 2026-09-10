using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Events;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Kompaz.Application.Authentication.EventHandlers;

/// <summary>
/// Delivers the sign-in link whose issue was recorded on the user.
/// </summary>
internal sealed class SendMagicLinkEmailHandler : INotificationHandler<MagicLinkIssuedEvent>
{
	private readonly IAuthenticationEmailSender _emailSender;
	private readonly ILogger<SendMagicLinkEmailHandler> _logger;

	public SendMagicLinkEmailHandler(
		IAuthenticationEmailSender emailSender,
		ILogger<SendMagicLinkEmailHandler> logger)
	{
		_emailSender = emailSender;
		_logger = logger;
	}

	/// <summary>
	/// Sends the link, and reports rather than rethrows when the relay will not take it.
	/// <para>
	/// <strong>A failure here must not reach the caller.</strong> Domain events are published inside
	/// <c>SaveChangesAsync</c>, so an exception would surface as a 500 from <c>POST /api/auth/magic-link</c> — but
	/// only for an address that has an account, because an unknown address never gets this far. A degraded relay
	/// would therefore turn the endpoint into an account-existence oracle and break the promise that the same
	/// answer comes back either way. The link is already in the database and asking for another one costs nothing,
	/// so the user losing this one is the cheaper failure.
	/// </para>
	/// <para>
	/// Deliberately not done for <see cref="SendInvitationEmailHandler"/>: an invitation is an administrator's
	/// action against an address they typed themselves, there is nothing to conceal from them, and they are the one
	/// person who can usefully act on "that did not send".
	/// </para>
	/// </summary>
	[SuppressMessage(
		"Design",
		"CA1031:Do not catch general exception types",
		Justification = "Any delivery failure whatsoever must produce the same response as an unknown address.")]
	[SuppressMessage(
		"Major Code Smell",
		"S2221:\"Exception\" should not be caught when not required by called methods",
		Justification = "Any delivery failure whatsoever must produce the same response as an unknown address.")]
	public async Task Handle(MagicLinkIssuedEvent notification, CancellationToken cancellationToken)
	{
		try
		{
			await _emailSender.SendMagicLinkAsync(
				notification.Email,
				notification.Name,
				notification.Token,
				cancellationToken);
		}
		catch (Exception exception)
		{
			// The address is not logged: this line fires precisely when somebody is asking about an address that
			// does have an account, so the log would become the enumeration answer the endpoint refuses to give.
			_logger.LogError(
				exception,
				"Failed to deliver a sign-in link to user {UserId}. The link remains valid and can be requested again.",
				notification.UserId);
		}
	}
}
