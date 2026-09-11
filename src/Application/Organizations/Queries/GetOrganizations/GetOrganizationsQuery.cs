using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Models;
using Kompaz.Application.Common.Search;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;

namespace Kompaz.Application.Organizations.Queries.GetOrganizations;

/// <summary>
/// Returns a page of organizations. Platform administrators see every tenant; everyone else sees only their own.
/// </summary>
[Authorize]
public record GetOrganizationsQuery : PagedQuery, IRequest<PaginatedList<OrganizationDto>>
{
	/// <summary>
	/// Gets an optional case-insensitive fragment of the organization name to filter on.
	/// </summary>
	public string? Search { get; init; }
}

public class GetOrganizationsQueryValidator : PagedQueryValidator<GetOrganizationsQuery>
{
	public GetOrganizationsQueryValidator()
	{
		RuleFor(query => query.Search)
			.MaximumLength(200);
	}
}

public class GetOrganizationsQueryHandler : IRequestHandler<GetOrganizationsQuery, PaginatedList<OrganizationDto>>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _user;

	public GetOrganizationsQueryHandler(IApplicationDbContext context, IUser user)
	{
		_context = context;
		_user = user;
	}

	public Task<PaginatedList<OrganizationDto>> Handle(GetOrganizationsQuery request, CancellationToken cancellationToken)
	{
		IQueryable<Organization> query = _context.Organizations.AsNoTracking();

		if (!OrganizationAccess.IsPlatformAdministrator(_user))
		{
			var organizationId = OrganizationAccess.ResolveTarget(_user, null);
			query = query.Where(organization => organization.Id == organizationId);
		}

		if (!string.IsNullOrWhiteSpace(request.Search))
		{
			string search = request.Search.Trim();
			string pattern = SearchPattern.Contains(search);

			// NormalizedName is already upper-cased, which is what it is for, so the fold the pattern expects on
			// both sides costs nothing here and the index on it is usable.
			query = query.Where(organization =>
				EF.Functions.Like(organization.NormalizedName, pattern, SearchPattern.EscapeCharacter));
		}

		return PaginatedList<OrganizationDto>.CreateAsync(
			query
				.OrderBy(organization => organization.Name)
				.ThenBy(organization => organization.Id)
				.Select(OrganizationDto.Projection),
			request.PageNumber,
			request.PageSize,
			cancellationToken);
	}
}
