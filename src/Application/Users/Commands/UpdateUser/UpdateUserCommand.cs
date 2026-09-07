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
	private readonly TimeProvider _timeProvider;

	public UpdateUserCommandHandler(IApplicationDbContext context, IUser currentUser, TimeProvider timeProvider)
	{
		_context = context;
		_currentUser = currentUser;
		_timeProvider = timeProvider;
	}

	public async Task<UserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
	{
		var user = await _context.Users
			.SingleOrDefaultAsync(candidate => candidate.Id == request.Id, cancellationToken)
			?? throw new NotFoundException(nameof(User), request.Id);

		OrganizationAccess.EnsureCanManage(_currentUser, user.OrganizationId);

		if (request.Role != user.Role)
		{
			OrganizationAccess.EnsureCanManageRole(_currentUser, request.Role);
			OrganizationAccess.EnsureCanManageRole(_currentUser, user.Role);
		}

		user.Update(request.Name, request.Role, _timeProvider.GetUtcNow());
		await _context.SaveChangesAsync(cancellationToken);

		return await _context.Users
			.AsNoTracking()
			.Where(candidate => candidate.Id == user.Id)
			.Select(UserDto.Projection)
			.SingleAsync(cancellationToken);
	}
}
