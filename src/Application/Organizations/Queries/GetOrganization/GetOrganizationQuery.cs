using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;

namespace Kompaz.Application.Organizations.Queries.GetOrganization;

/// <summary>
/// Returns a single organization. Callers may only read the organization they belong to.
/// </summary>
[Authorize]
public record GetOrganizationQuery(Guid Id) : IRequest<OrganizationDto>;

public class GetOrganizationQueryHandler : IRequestHandler<GetOrganizationQuery, OrganizationDto>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _user;

	public GetOrganizationQueryHandler(IApplicationDbContext context, IUser user)
	{
		_context = context;
		_user = user;
	}

	public async Task<OrganizationDto> Handle(GetOrganizationQuery request, CancellationToken cancellationToken)
	{
		OrganizationAccess.EnsureCanRead(_user, request.Id);

		return await _context.Organizations
			.AsNoTracking()
			.Where(organization => organization.Id == request.Id)
			.Select(OrganizationDto.Projection)
			.SingleOrDefaultAsync(cancellationToken)
			?? throw new NotFoundException(nameof(Organization), request.Id);
	}
}
