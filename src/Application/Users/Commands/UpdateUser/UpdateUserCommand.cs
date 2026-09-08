using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Users.Commands.UpdateUser;

/// <summary>
/// Changes a user's display name and role. The email address is the sign-in identity and is not editable.
/// </summary>
[Authorize(MinimumRole = UserRole.Administrator)]
public record UpdateUserCommand(Guid Id, string Name, UserRole Role) : IRequest<UserDto>;

public class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
	public UpdateUserCommandValidator()
	{
		RuleFor(command => command.Id)
			.NotEqual(Guid.Empty);

		RuleFor(command => command.Name)
			.NotEmpty()
			.MaximumLength(200);

		RuleFor(command => command.Role)
			.IsInEnum();
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
			.SingleOrDefaultAsync(candidate => candidate.Id == request.Id, cancellationToken)
			?? throw new NotFoundException(nameof(User), request.Id);

		OrganizationAccess.EnsureCanManage(_currentUser, user.OrganizationId);

		// Checked whatever the request asks for, not only when the role moves: editing a platform administrator at
		// all is reserved, the way deleting one is. Otherwise an ordinary administrator could rename one.
		OrganizationAccess.EnsureCanManageRole(_currentUser, user.Role);

		if (request.Role != user.Role)
		{
			OrganizationAccess.EnsureCanManageRole(_currentUser, request.Role);

			await EnsureAPlatformAdministratorRemainsAsync(user, request.Role, cancellationToken);
		}

		user.Update(request.Name, request.Role);
		await _context.SaveChangesAsync(cancellationToken);

		return await _context.Users
			.AsNoTracking()
			.Where(candidate => candidate.Id == user.Id)
			.Select(UserDto.Projection)
			.SingleAsync(cancellationToken);
	}

	/// <summary>
	/// Refuses a demotion that would leave the platform with nobody able to grant the role back. Deleting the last
	/// platform administrator is already impossible — only a platform administrator may remove one, and nobody may
	/// remove themselves — so giving up the role is the one remaining way to lock everybody out.
	/// </summary>
	private async Task EnsureAPlatformAdministratorRemainsAsync(User user, UserRole newRole, CancellationToken cancellationToken)
	{
		if (user.Role != UserRole.PlatformAdministrator || newRole == UserRole.PlatformAdministrator)
		{
			return;
		}

		bool anotherRemains = await _context.Users
			.AnyAsync(
				candidate => candidate.Id != user.Id && candidate.Role == UserRole.PlatformAdministrator,
				cancellationToken);

		if (!anotherRemains)
		{
			throw new ConflictException(
				"The last platform administrator cannot give up the role. Appoint another one first.");
		}
	}
}
