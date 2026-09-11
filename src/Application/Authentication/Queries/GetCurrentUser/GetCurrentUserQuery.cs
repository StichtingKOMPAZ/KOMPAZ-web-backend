using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Application.Users;

namespace Kompaz.Application.Authentication.Queries.GetCurrentUser;

/// <summary>
/// Returns the profile behind the bearer token on the current request.
/// </summary>
[Authorize]
public record GetCurrentUserQuery : IRequest<UserDto>;

public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, UserDto>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _user;

	public GetCurrentUserQueryHandler(IApplicationDbContext context, IUser user)
	{
		_context = context;
		_user = user;
	}

	public async Task<UserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
	{
		var userId = _user.Id ?? throw new UnauthorizedAccessException();

		return await _context.Users
			.AsNoTracking()
			.Where(user => user.Id == userId && user.DeletedUtc == null)
			.Select(UserDto.Projection)
			.SingleOrDefaultAsync(cancellationToken)
			?? throw new AuthenticationFailedException("Deze gebruiker bestaat niet meer.");
	}
}
