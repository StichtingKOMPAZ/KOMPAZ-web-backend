using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Events;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Kompaz.Application.Users.EventHandlers;

/// <summary>
/// Tells somebody that their account has been deleted.
/// </summary>
internal sealed class SendAccountDeletedEmailHandler : INotificationHandler<UserDeletedEvent>
{
	private readonly IAuthenticationEmailSender _emailSender;
	private readonly ILogger<SendAccountDeletedEmailHandler> _logger;

	public SendAccountDeletedEmailHandler(
		IAuthenticationEmailSender emailSender,
		ILogger<SendAccountDeletedEmailHandler> logger)
	{
		_emailSender = emailSender;
		_logger = logger;
	}

	/// <summary>
	/// Sends the notice, and reports rather than rethrows when the relay will not take it.
	/// <para>
	/// Unlike a sign-in link, this one cannot be asked for again. The deletion is already committed by the time
	/// this runs, so an exception would answer the administrator with a 500 for a request that did in fact
	/// succeed — and their obvious response, deleting again, now returns 404. Reporting the address here is fine
	/// and useful: an administrator typed it, and it discloses nothing they did not already have on screen.
	/// </para>
	/// </summary>
	[SuppressMessage(
		"Design",
		"CA1031:Do not catch general exception types",
		Justification = "The deletion is already committed; a delivery failure must not report it as failed.")]
	[SuppressMessage(
		"Major Code Smell",
		"S2221:\"Exception\" should not be caught when not required by called methods",
		Justification = "The deletion is already committed; a delivery failure must not report it as failed.")]
	public async Task Handle(UserDeletedEvent notification, CancellationToken cancellationToken)
	{
		try
		{
			await _emailSender.SendAccountDeletedAsync(notification.Email, notification.Name, cancellationToken);
		}
		catch (Exception exception)
		{
			_logger.LogError(
				exception,
				"Failed to tell {Email} that their account was deleted. The account is deleted regardless.",
				notification.Email);
		}
	}
}
