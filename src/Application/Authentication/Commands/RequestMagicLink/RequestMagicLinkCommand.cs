using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Kompaz.Application.Authentication.Commands.RequestMagicLink;

/// <summary>
/// Emails a sign-in link to the address supplied. Succeeds whether or not the address belongs to a user,
/// so the endpoint cannot be used to discover who has an account.
/// </summary>
public record RequestMagicLinkCommand(string Email) : IRequest;

public class RequestMagicLinkCommandValidator : AbstractValidator<RequestMagicLinkCommand>
{
	public RequestMagicLinkCommandValidator()
	{
		RuleFor(command => command.Email)
			.NotEmpty()
			.MaximumLength(320)
			.EmailAddress();
	}
}

public class RequestMagicLinkCommandHandler : IRequestHandler<RequestMagicLinkCommand>
{
	private readonly IApplicationDbContext _context;
	private readonly LoginTokenIssuer _tokenIssuer;
	private readonly IAuthenticationEmailSender _emailSender;
	private readonly ILogger<RequestMagicLinkCommandHandler> _logger;

	public RequestMagicLinkCommandHandler(
		IApplicationDbContext context,
		LoginTokenIssuer tokenIssuer,
		IAuthenticationEmailSender emailSender,
		ILogger<RequestMagicLinkCommandHandler> logger)
	{
		_context = context;
		_tokenIssuer = tokenIssuer;
		_emailSender = emailSender;
		_logger = logger;
	}

	public async Task Handle(RequestMagicLinkCommand request, CancellationToken cancellationToken)
	{
		string normalizedEmail = User.Normalize(request.Email);

		var user = await _context.Users
			.SingleOrDefaultAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);

		if (user is null)
		{
			_logger.LogInformation("Magic link requested for an address without an account.");
			return;
		}

		var purpose = user.Status == UserStatus.Invited
			? LoginTokenPurpose.Invitation
			: LoginTokenPurpose.MagicLink;

		string token = await _tokenIssuer.IssueAsync(user, purpose, cancellationToken);
		await _context.SaveChangesAsync(cancellationToken);

		if (purpose == LoginTokenPurpose.Invitation)
		{
			string organizationName = await _context.Organizations
				.Where(organization => organization.Id == user.OrganizationId)
				.Select(organization => organization.Name)
				.SingleAsync(cancellationToken);

			await _emailSender.SendInvitationAsync(user.Email, user.Name, organizationName, token, cancellationToken);
			return;
		}

		await _emailSender.SendMagicLinkAsync(user.Email, user.Name, token, cancellationToken);
	}
}
