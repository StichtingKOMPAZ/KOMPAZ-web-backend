using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Events;

namespace Kompaz.Application.Authentication.EventHandlers;

/// <summary>
/// Delivers the sign-in link whose issue was recorded on the user.
/// </summary>
internal sealed class SendMagicLinkEmailHandler : INotificationHandler<MagicLinkIssuedEvent>
{
	private readonly IAuthenticationEmailSender _emailSender;

	public SendMagicLinkEmailHandler(IAuthenticationEmailSender emailSender)
	{
		_emailSender = emailSender;
	}

	public Task Handle(MagicLinkIssuedEvent notification, CancellationToken cancellationToken) =>
		_emailSender.SendMagicLinkAsync(
			notification.Email,
			notification.Name,
			notification.Token,
			cancellationToken);
}
