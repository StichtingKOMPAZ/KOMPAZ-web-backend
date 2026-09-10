using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Users.Commands.UpdateUser;

/// <summary>
/// Changes a user's name, email address, role and organization, as an administrator.
/// <para>
/// <see cref="Name"/> and <see cref="Email"/> are always supplied; <see cref="Role"/> and
/// <see cref="OrganizationId"/> are optional and mean "leave this alone" when omitted. That split follows who is
/// editing: name and email are on every editor's form, so every request carries them, while role and organization
/// only appear for a platform administrator — so a request without them is saying they were never its to change,
/// rather than asking for a default.
/// </para>
/// </summary>
[Authorize(MinimumRole = UserRole.Administrator)]
public record UpdateUserCommand(
	Guid Id,
	string Name,
	string Email,
	UserRole? Role = null,
	Guid? OrganizationId = null) : IRequest<UserDto>;

public class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
	public UpdateUserCommandValidator()
	{
		RuleFor(command => command.Id)
			.NotEqual(Guid.Empty);

		RuleFor(command => command.Name)
			.NotEmpty()
			.MaximumLength(200);

		RuleFor(command => command.Email)
			.NotEmpty()
			.MaximumLength(320)
			.EmailAddress();

		RuleFor(command => command.Role)
			.IsInEnum()
			.When(command => command.Role.HasValue);

		RuleFor(command => command.OrganizationId)
			.NotEqual(Guid.Empty)
			.When(command => command.OrganizationId.HasValue);
	}
}

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, UserDto>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _currentUser;

	public UpdateUserCommandHandler(IApplicationDbContext context, IUser currentUser)
	{
		_context = context;
		_currentUser = currentUser;
	}

	public async Task<UserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
	{
		var user = await _context.Users
			.Include(candidate => candidate.Organization)
			.SingleOrDefaultAsync(candidate => candidate.Id == request.Id && candidate.DeletedUtc == null, cancellationToken)
			?? throw new NotFoundException(nameof(User), request.Id);

		OrganizationAccess.EnsureCanManage(_currentUser, user.OrganizationId);

		// Checked whatever the request asks for, not only when the role moves: editing a platform administrator at
		// all is reserved, the way deleting one is. Otherwise an ordinary administrator could rename one.
		OrganizationAccess.EnsureCanManageRole(_currentUser, user.Role);

		if (request.OrganizationId is { } destination && destination != user.OrganizationId)
		{
			await MoveAsync(user, destination, request.Role, cancellationToken);
		}
		else if (request.Role is { } role && role != user.Role)
		{
			await ChangeRoleAsync(user, role, cancellationToken);
		}

		await ChangeEmailAsync(user, request.Email, cancellationToken);

		user.Rename(request.Name);
		await _context.SaveChangesAsync(cancellationToken);

		return await _context.Users
			.AsNoTracking()
			.Where(candidate => candidate.Id == user.Id)
			.Select(UserDto.Projection)
			.SingleAsync(cancellationToken);
	}

	/// <summary>
	/// Moves the user into another organization. Reaching two organizations at once is a platform administrator's
	/// privilege, so managing both ends is the whole check.
	/// </summary>
	private async Task MoveAsync(User user, Guid destination, UserRole? requestedRole, CancellationToken cancellationToken)
	{
		OrganizationAccess.EnsureCanManage(_currentUser, destination);

		// A move resets the role, so a request that also names one is contradicting itself. Refused rather than
		// quietly overruled: silently ignoring half of what somebody asked for is how a caller learns to distrust
		// the response. Promoting them in the new organization is the next request.
		if (requestedRole is { } role && role != UserRole.Member)
		{
			throw new ConflictException(
				$"Moving a user resets their role to {UserRole.Member}. Grant a higher role after the move.");
		}

		var organization = await _context.Organizations
			.SingleOrDefaultAsync(candidate => candidate.Id == destination, cancellationToken)
			?? throw new NotFoundException(nameof(Organization), destination);

		// Both pools the move empties — the organization they are leaving, and the platform role they give up by
		// leaving it — are checked before anything changes.
		if (user.Role >= UserRole.Administrator)
		{
			await AdministratorCoverage.EnsureAnAdministratorRemainsAsync(
				_context, user.OrganizationId, user.Id, cancellationToken);
		}

		if (user.Role == UserRole.PlatformAdministrator)
		{
			await AdministratorCoverage.EnsureAPlatformAdministratorRemainsAsync(_context, user.Id, cancellationToken);
		}

		user.MoveTo(organization.Id);
		user.Organization = organization;

		// Their sessions belong to the organization they were in: every access token they hold names the old one,
		// and a role they no longer have. Ending the sessions makes them sign in again and come back with claims
		// that match the row.
		await _context.RefreshTokens
			.Where(token => token.UserId == user.Id)
			.ExecuteDeleteAsync(cancellationToken);
	}

	private async Task ChangeRoleAsync(User user, UserRole role, CancellationToken cancellationToken)
	{
		// Who administers an organization is the platform's call, so an organization administrator edits names and
		// addresses and nothing else. Granting is looser at invitation time, where a member is being created
		// rather than an existing person moved between roles.
		OrganizationAccess.EnsureCanChangeRole(_currentUser);
		OrganizationAccess.EnsureCanHoldRole(user.Organization, role);

		if (user.Role == UserRole.PlatformAdministrator)
		{
			await AdministratorCoverage.EnsureAPlatformAdministratorRemainsAsync(_context, user.Id, cancellationToken);
		}

		// A demotion empties the same pool a deletion would, and leaves the organization just as stuck.
		if (user.Role >= UserRole.Administrator && role < UserRole.Administrator)
		{
			await AdministratorCoverage.EnsureAnAdministratorRemainsAsync(
				_context, user.OrganizationId, user.Id, cancellationToken);
		}

		user.Role = role;
	}

	/// <summary>
	/// Points the account at a new address, which an administrator is trusted to have confirmed belongs to the
	/// person in front of them. The unique index has the final say on a duplicate; asking first only buys the
	/// caller a message that names the address instead of a bare conflict.
	/// </summary>
	private async Task ChangeEmailAsync(User user, string email, CancellationToken cancellationToken)
	{
		string normalizedEmail = User.Normalize(email);

		if (string.Equals(normalizedEmail, user.NormalizedEmail, StringComparison.Ordinal))
		{
			return;
		}

		bool taken = await _context.Users
			.AnyAsync(candidate => candidate.Id != user.Id && candidate.NormalizedEmail == normalizedEmail, cancellationToken);

		if (taken)
		{
			throw new ConflictException($"A user with the email address \"{email.Trim()}\" already exists.");
		}

		// Any link outstanding for this account was emailed to the address they are leaving, and would sign
		// whoever still reads that inbox into an account that is no longer theirs. Sessions are left alone: the
		// person holding them has not changed, only where their post goes.
		await _context.LoginTokens
			.Where(token => token.UserId == user.Id)
			.ExecuteDeleteAsync(cancellationToken);

		user.ChangeEmail(email);
	}
}
