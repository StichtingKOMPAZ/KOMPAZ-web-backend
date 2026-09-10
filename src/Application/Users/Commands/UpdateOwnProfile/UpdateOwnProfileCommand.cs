using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;

namespace Kompaz.Application.Users.Commands.UpdateOwnProfile;

/// <summary>
/// Lets any signed-in user edit their own profile, without the administrator rights that
/// <see cref="Kompaz.Application.Users.Commands.UpdateUser.UpdateUserCommand"/> requires.
/// <para>
/// Only the display name moves here. The email address is the sign-in identity and changing it needs proof of the
/// new inbox, and a role is never self-assignable.
/// </para>
/// </summary>
[Authorize]
public record UpdateOwnProfileCommand(string Name) : IRequest<UserDto>;

public class UpdateOwnProfileCommandValidator : AbstractValidator<UpdateOwnProfileCommand>
{
	public UpdateOwnProfileCommandValidator()
	{
		RuleFor(command => command.Name)
			.NotEmpty()
			.MaximumLength(200);
	}
}

public class UpdateOwnProfileCommandHandler : IRequestHandler<UpdateOwnProfileCommand, UserDto>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _currentUser;

	public UpdateOwnProfileCommandHandler(IApplicationDbContext context, IUser currentUser)
	{
		_context = context;
		_currentUser = currentUser;
	}

	public async Task<UserDto> Handle(UpdateOwnProfileCommand request, CancellationToken cancellationToken)
	{
		var userId = _currentUser.Id ?? throw new UnauthorizedAccessException();

		var user = await _context.Users
			.SingleOrDefaultAsync(candidate => candidate.Id == userId && candidate.DeletedUtc == null, cancellationToken)
			?? throw new AuthenticationFailedException("The authenticated user no longer exists.");

		user.Rename(request.Name);
		await _context.SaveChangesAsync(cancellationToken);

		return await _context.Users
			.AsNoTracking()
			.Where(candidate => candidate.Id == user.Id)
			.Select(UserDto.Projection)
			.SingleAsync(cancellationToken);
	}
}
