using Kompaz.Application.Authentication;
using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using Kompaz.Domain.Events;

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
	private readonly TimeProvider _timeProvider;

	public ResendUserInvitationCommandHandler(
		IApplicationDbContext context,
		IUser currentUser,
		LoginTokenIssuer tokenIssuer,
		TimeProvider timeProvider)
	{
		_context = context;
		_currentUser = currentUser;
		_tokenIssuer = tokenIssuer;
		_timeProvider = timeProvider;
	}

	public async Task Handle(ResendUserInvitationCommand request, CancellationToken cancellationToken)
	{
		var user = await _context.Users
			.Include(candidate => candidate.Organization)
			.SingleOrDefaultAsync(candidate => candidate.Id == request.Id && candidate.DeletedUtc == null, cancellationToken)
			?? throw new NotFoundException(nameof(User), request.Id);

		OrganizationAccess.EnsureCanManage(_currentUser, user.OrganizationId);
		OrganizationAccess.EnsureCanManageRole(_currentUser, user.Role);

		if (user.Status == UserStatus.Active)
		{
			throw new ConflictException("The invitation has already been accepted.");
		}

		string token = await _tokenIssuer.IssueAsync(user, LoginTokenPurpose.Invitation, cancellationToken);
		user.RecordInvitationSent(_timeProvider.GetUtcNow());
		user.AddDomainEvent(new InvitationIssuedEvent(user.Id, user.Email, user.Name, user.Organization.Name, token));

		await _context.SaveChangesAsync(cancellationToken);
	}
}
