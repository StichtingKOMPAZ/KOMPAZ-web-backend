using Kompaz.Application.Authentication;
using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Users.Commands.ResendUserInvitation;

/// <summary>
/// Issues a fresh invitation link for a user who has not signed in yet, retiring any link sent earlier.
/// </summary>
[Authorize(MinimumRole = UserRole.Administrator)]
public record ResendUserInvitationCommand(Guid Id) : IRequest;

public class ResendUserInvitationCommandValidator : AbstractValidator<ResendUserInvitationCommand>
{
	public ResendUserInvitationCommandValidator()
	{
		RuleFor(command => command.Id)
			.NotEqual(Guid.Empty);
	}
}

public class ResendUserInvitationCommandHandler : IRequestHandler<ResendUserInvitationCommand>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _currentUser;
	private readonly LoginTokenIssuer _tokenIssuer;
	private readonly IAuthenticationEmailSender _emailSender;
	private readonly TimeProvider _timeProvider;

	public ResendUserInvitationCommandHandler(
		IApplicationDbContext context,
		IUser currentUser,
		LoginTokenIssuer tokenIssuer,
		IAuthenticationEmailSender emailSender,
		TimeProvider timeProvider)
	{
		_context = context;
		_currentUser = currentUser;
		_tokenIssuer = tokenIssuer;
		_emailSender = emailSender;
		_timeProvider = timeProvider;
	}

	public async Task Handle(ResendUserInvitationCommand request, CancellationToken cancellationToken)
	{
		var user = await _context.Users
			.Include(candidate => candidate.Organization)
			.SingleOrDefaultAsync(candidate => candidate.Id == request.Id, cancellationToken)
			?? throw new NotFoundException(nameof(User), request.Id);

		OrganizationAccess.EnsureCanManage(_currentUser, user.OrganizationId);
		OrganizationAccess.EnsureCanManageRole(_currentUser, user.Role);

		if (user.Status == UserStatus.Active)
		{
			throw new ConflictException("The invitation has already been accepted.");
		}

		string token = await _tokenIssuer.IssueAsync(user, LoginTokenPurpose.Invitation, cancellationToken);
		user.RecordInvitationSent(_timeProvider.GetUtcNow());

		await _context.SaveChangesAsync(cancellationToken);

		await _emailSender.SendInvitationAsync(user.Email, user.Name, user.Organization.Name, token, cancellationToken);
	}
}
