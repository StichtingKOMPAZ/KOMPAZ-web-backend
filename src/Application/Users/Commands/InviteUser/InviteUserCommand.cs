using Kompaz.Application.Authentication;
using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using Kompaz.Domain.Events;

namespace Kompaz.Application.Users.Commands.InviteUser;

/// <summary>
/// Creates an invited user and emails them a link that both accepts the invitation and signs them in.
/// Administrators invite into their own organization; platform administrators may target any organization.
/// </summary>
[Authorize(MinimumRole = UserRole.Administrator)]
public record InviteUserCommand(string Email, string Name, UserRole Role = UserRole.Member, Guid? OrganizationId = null)
	: IRequest<UserDto>;

public class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
	public InviteUserCommandValidator()
	{
		RuleFor(command => command.Email)
			.NotEmpty()
			.MaximumLength(320)
			.EmailAddress();

		RuleFor(command => command.Name)
			.NotEmpty()
			.MaximumLength(200);

		RuleFor(command => command.Role)
			.IsInEnum();

		RuleFor(command => command.OrganizationId)
			.NotEqual(Guid.Empty)
			.When(command => command.OrganizationId.HasValue);
	}
}

public class InviteUserCommandHandler : IRequestHandler<InviteUserCommand, UserDto>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _currentUser;
	private readonly LoginTokenIssuer _tokenIssuer;
	private readonly TimeProvider _timeProvider;

	public InviteUserCommandHandler(
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

	public async Task<UserDto> Handle(InviteUserCommand request, CancellationToken cancellationToken)
	{
		var organizationId = OrganizationAccess.ResolveTarget(_currentUser, request.OrganizationId);
		OrganizationAccess.EnsureCanManage(_currentUser, organizationId);
		OrganizationAccess.EnsureCanGrantRole(_currentUser, request.Role);

		var organization = await _context.Organizations
			.SingleOrDefaultAsync(candidate => candidate.Id == organizationId, cancellationToken)
			?? throw new NotFoundException(nameof(Organization), organizationId);

		OrganizationAccess.EnsureCanHoldRole(organization, request.Role);

		string normalizedEmail = User.Normalize(request.Email);
		var now = _timeProvider.GetUtcNow();

		var existing = await _context.Users
			.SingleOrDefaultAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);

		var user = existing is null
			? AddInvitedUser(organizationId, request, now)
			: RenewInvitation(existing, organizationId, request, now);

		string token = await _tokenIssuer.IssueAsync(user, LoginTokenPurpose.Invitation, cancellationToken);
		user.AddDomainEvent(new InvitationIssuedEvent(user.Id, user.Email, user.Name, organization.Name, token));

		await _context.SaveChangesAsync(cancellationToken);

		return await _context.Users
			.AsNoTracking()
			.Where(candidate => candidate.Id == user.Id)
			.Select(UserDto.Projection)
			.SingleAsync(cancellationToken);
	}

	private User AddInvitedUser(Guid organizationId, InviteUserCommand request, DateTimeOffset now)
	{
		var user = User.Invite(organizationId, request.Email, request.Name, request.Role, now);
		_context.Users.Add(user);

		return user;
	}

	/// <summary>
	/// Re-invites somebody who has not accepted yet, rather than refusing the address outright. The user row commits
	/// before the invitation email goes out, so a send that fails would otherwise leave a user who was never told and
	/// an address the administrator can no longer invite. Repeating the request is the obvious recovery, so it works.
	/// <para>
	/// A deleted user is reached the same way, and comes back as a fresh invitation. Their row is still here holding
	/// the address, which is the unique key, so the alternative would be that deleting somebody burns their email
	/// address for good. Reusing the row also keeps every log and audit entry pointing at one person instead of
	/// splitting them across two identifiers — which is the reason the row was kept in the first place.
	/// </para>
	/// <para>
	/// An address belonging to somebody who has already signed in and is still here is a genuine clash and is still
	/// refused, as is one in an organization the caller did not name — with the message the caller would have got
	/// before either way, so this discloses nothing new about who exists.
	/// </para>
	/// </summary>
	private User RenewInvitation(User existing, Guid organizationId, InviteUserCommand request, DateTimeOffset now)
	{
		bool clashes = existing.OrganizationId != organizationId
			|| (existing.Status == UserStatus.Active && !existing.IsDeleted);

		if (clashes)
		{
			throw new ConflictException($"A user with the email address \"{request.Email.Trim()}\" already exists.");
		}

		OrganizationAccess.EnsureCanManageRole(_currentUser, existing.Role);

		if (existing.IsDeleted)
		{
			existing.ReviveAsInvited(request.Name, request.Role, now);

			return existing;
		}

		existing.Update(request.Name, request.Role);
		existing.RecordInvitationSent(now);

		return existing;
	}
}
