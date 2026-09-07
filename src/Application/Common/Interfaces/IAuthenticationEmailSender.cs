namespace Kompaz.Application.Common.Interfaces;

/// <summary>
/// Delivers the emails that carry sign-in links. Link formatting and transport live in the infrastructure layer.
/// </summary>
public interface IAuthenticationEmailSender
{
	Task SendMagicLinkAsync(string email, string name, string token, CancellationToken cancellationToken = default);

	Task SendInvitationAsync(string email, string name, string organizationName, string token, CancellationToken cancellationToken = default);
}
