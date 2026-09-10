using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Users.Commands.RestoreUser;

/// <summary>
/// Undoes a deletion, putting the user back exactly as they were.
/// <para>
/// It does not hand them a way in. Deleting removed their sign-in links and sessions, and this does not recreate
/// them, so a restored user starts from the login page like anybody else. Restoring somebody who is not deleted is
/// not an error: an administrator clicking twice, or two of them reaching for the same undo, should both be told
/// the account is back rather than one of them being told they were too late.
/// </para>
/// </summary>
[Authorize(MinimumRole = UserRole.Administrator)]
public record RestoreUserCommand(Guid Id) : IRequest<UserDto>;

public class RestoreUserCommandValidator : AbstractValidator<RestoreUserCommand>
{
	public RestoreUserCommandValidator()
	{
		RuleFor(command => command.Id)
			.NotEqual(Guid.Empty);
	}
}

public class RestoreUserCommandHandler : IRequestHandler<RestoreUserCommand, UserDto>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _currentUser;

	public RestoreUserCommandHandler(IApplicationDbContext context, IUser currentUser)
	{
		_context = context;
		_currentUser = currentUser;
	}

	public async Task<UserDto> Handle(RestoreUserCommand request, CancellationToken cancellationToken)
	{
		// Deliberately not filtered on DeletedUtc: this is the one command whose whole subject is a deleted row.
		var user = await _context.Users
			.SingleOrDefaultAsync(candidate => candidate.Id == request.Id, cancellationToken)
			?? throw new NotFoundException(nameof(User), request.Id);

		OrganizationAccess.EnsureCanManage(_currentUser, user.OrganizationId);
		OrganizationAccess.EnsureCanManageRole(_currentUser, user.Role);

		if (user.IsDeleted)
		{
			user.Restore();
			await _context.SaveChangesAsync(cancellationToken);
		}

		return await _context.Users
			.AsNoTracking()
			.Where(candidate => candidate.Id == user.Id)
			.Select(UserDto.Projection)
			.SingleAsync(cancellationToken);
	}
}
