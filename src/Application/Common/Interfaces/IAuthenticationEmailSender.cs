namespace Kompaz.Application.Common.Interfaces;

/// <summary>
/// Delivers the emails this application sends about somebody's account: the two that carry a sign-in link, and
/// the notice that an account is gone. Wording, link formatting and transport all live in the infrastructure layer.
/// </summary>
public interface IAuthenticationEmailSender
{
	Task SendMagicLinkAsync(string email, string name, string token, CancellationToken cancellationToken = default);

	Task SendInvitationAsync(string email, string name, string organizationName, string token, CancellationToken cancellationToken = default);

	/// <summary>
	/// Tells somebody their account has been deleted. Carries no link: there is nothing left for them to open.
	/// </summary>
	Task SendAccountDeletedAsync(string email, string name, CancellationToken cancellationToken = default);
}
