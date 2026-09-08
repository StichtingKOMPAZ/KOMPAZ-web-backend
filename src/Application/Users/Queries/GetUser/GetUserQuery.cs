using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;

namespace Kompaz.Application.Users.Queries.GetUser;

/// <summary>
/// Returns a single user. Anybody may read their own profile; reading somebody else needs administrator rights over
/// their organization, so this endpoint cannot be used to walk the roster that <c>GET /api/users</c> gates.
/// </summary>
[Authorize]
public record GetUserQuery(Guid Id) : IRequest<UserDto>;

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _currentUser;

	public GetUserQueryHandler(IApplicationDbContext context, IUser currentUser)
	{
		_context = context;
		_currentUser = currentUser;
	}

	public async Task<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken)
	{
		var user = await _context.Users
			.AsNoTracking()
			.Where(candidate => candidate.Id == request.Id)
			.Select(UserDto.Projection)
			.SingleOrDefaultAsync(cancellationToken)
			?? throw new NotFoundException(nameof(User), request.Id);

		if (_currentUser.Id != user.Id)
		{
			OrganizationAccess.EnsureCanManage(_currentUser, user.OrganizationId);
		}

		return user;
	}
}
