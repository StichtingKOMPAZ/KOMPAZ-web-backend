using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;

namespace Kompaz.Application.Users.Queries.GetUser;

/// <summary>
/// Returns a single user. Callers may read themselves and anyone inside their own organization.
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
			OrganizationAccess.EnsureCanRead(_currentUser, user.OrganizationId);
		}

		return user;
	}
}
