using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Events;

namespace Kompaz.Application.Authentication.EventHandlers;

/// <summary>
/// Delivers the invitation whose issue was recorded on the user. The commands that issue one — inviting somebody and
/// re-inviting them — do not send it themselves, so that "an invitation was issued" and "an invitation was sent"
/// stay separable, and neither command has to know about email.
/// </summary>
internal sealed class SendInvitationEmailHandler : INotificationHandler<InvitationIssuedEvent>
{
	private readonly IAuthenticationEmailSender _emailSender;

	public SendInvitationEmailHandler(IAuthenticationEmailSender emailSender)
	{
		_emailSender = emailSender;
	}

	public Task Handle(InvitationIssuedEvent notification, CancellationToken cancellationToken) =>
		_emailSender.SendInvitationAsync(
			notification.Email,
			notification.Name,
			notification.OrganizationName,
			notification.Token,
			cancellationToken);
}
