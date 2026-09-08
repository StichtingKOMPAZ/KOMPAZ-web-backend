using Kompaz.Application.Authentication;
using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

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
	private readonly IAuthenticationEmailSender _emailSender;
	private readonly TimeProvider _timeProvider;

	public InviteUserCommandHandler(
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

	public async Task<UserDto> Handle(InviteUserCommand request, CancellationToken cancellationToken)
	{
		var organizationId = OrganizationAccess.ResolveTarget(_currentUser, request.OrganizationId);
		OrganizationAccess.EnsureCanManage(_currentUser, organizationId);
		OrganizationAccess.EnsureCanManageRole(_currentUser, request.Role);

		var organization = await _context.Organizations
			.SingleOrDefaultAsync(candidate => candidate.Id == organizationId, cancellationToken)
			?? throw new NotFoundException(nameof(Organization), organizationId);

		string normalizedEmail = User.Normalize(request.Email);
		var now = _timeProvider.GetUtcNow();

		var existing = await _context.Users
			.SingleOrDefaultAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);

		var user = existing is null
			? AddInvitedUser(organizationId, request, now)
			: RenewInvitation(existing, organizationId, request, now);

		string token = await _tokenIssuer.IssueAsync(user, LoginTokenPurpose.Invitation, cancellationToken);
		await _context.SaveChangesAsync(cancellationToken);

		await _emailSender.SendInvitationAsync(user.Email, user.Name, organization.Name, token, cancellationToken);

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
	/// An address belonging to somebody who has already signed in is a genuine clash and is still refused, as is one
	/// pending in an organization the caller did not name — with the message the caller would have got before either
	/// way, so this discloses nothing new about who exists.
	/// </para>
	/// </summary>
	private User RenewInvitation(User existing, Guid organizationId, InviteUserCommand request, DateTimeOffset now)
	{
		if (existing.Status == UserStatus.Active || existing.OrganizationId != organizationId)
		{
			throw new ConflictException($"A user with the email address \"{request.Email.Trim()}\" already exists.");
		}

		OrganizationAccess.EnsureCanManageRole(_currentUser, existing.Role);

		existing.Update(request.Name, request.Role, now);
		existing.RecordInvitationSent(now);

		return existing;
	}
}
