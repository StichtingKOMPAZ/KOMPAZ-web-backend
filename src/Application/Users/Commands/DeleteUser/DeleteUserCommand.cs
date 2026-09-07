using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Users.Commands.DeleteUser;

/// <summary>
/// Removes a user and any sign-in links outstanding for them.
/// </summary>
[Authorize(MinimumRole = UserRole.Administrator)]
public record DeleteUserCommand(Guid Id) : IRequest;

public class DeleteUserCommandValidator : AbstractValidator<DeleteUserCommand>
{
	public DeleteUserCommandValidator()
	{
		RuleFor(command => command.Id)
			.NotEqual(Guid.Empty);
	}
}

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _currentUser;

	public DeleteUserCommandHandler(IApplicationDbContext context, IUser currentUser)
	{
		_context = context;
		_currentUser = currentUser;
	}

	public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
	{
		var user = await _context.Users
			.SingleOrDefaultAsync(candidate => candidate.Id == request.Id, cancellationToken)
			?? throw new NotFoundException(nameof(User), request.Id);

		OrganizationAccess.EnsureCanManage(_currentUser, user.OrganizationId);
		OrganizationAccess.EnsureCanManageRole(_currentUser, user.Role);

		if (_currentUser.Id == user.Id)
		{
			throw new ConflictException("A user cannot delete their own account.");
		}

		_context.Users.Remove(user);
		await _context.SaveChangesAsync(cancellationToken);
	}
}
