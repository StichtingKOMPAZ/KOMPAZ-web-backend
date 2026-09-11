using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;

namespace Kompaz.Application.Organizations.Queries.GetOrganizationLogo;

/// <summary>
/// Returns an organization's logo, or <see langword="null"/> when there is none to serve and the caller should
/// show the placeholder. Callers may only read the organization they belong to.
/// <para>
/// Not paged, and no <c>Dto</c> list, because there is at most one: this is a single image fetched by a client
/// that already knows which organization it is looking at.
/// </para>
/// </summary>
[Authorize]
public record GetOrganizationLogoQuery(Guid Id) : IRequest<OrganizationLogoDto?>;

public class GetOrganizationLogoQueryValidator : AbstractValidator<GetOrganizationLogoQuery>
{
	public GetOrganizationLogoQueryValidator()
	{
		RuleFor(query => query.Id)
			.NotEqual(Guid.Empty);
	}
}

public class GetOrganizationLogoQueryHandler : IRequestHandler<GetOrganizationLogoQuery, OrganizationLogoDto?>
{
	private readonly IApplicationDbContext _context;
	private readonly IFileStore _files;
	private readonly IUser _user;

	public GetOrganizationLogoQueryHandler(IApplicationDbContext context, IFileStore files, IUser user)
	{
		_context = context;
		_files = files;
		_user = user;
	}

	public async Task<OrganizationLogoDto?> Handle(GetOrganizationLogoQuery request, CancellationToken cancellationToken)
	{
		OrganizationAccess.EnsureCanRead(_user, request.Id);

		// Asked separately, so an organization that is not there is a 404 rather than a placeholder. The two are
		// different answers: one means "no logo yet", the other means the client is pointing at nothing.
		if (!await _context.Organizations.AnyAsync(
			organization => organization.Id == request.Id, cancellationToken))
		{
			throw new NotFoundException(nameof(Organization), request.Id);
		}

		var stored = await _context.OrganizationLogos
			.AsNoTracking()
			.Where(logo => logo.OrganizationId == request.Id)
			.Select(logo => new { logo.StorageKey, logo.ContentType })
			.SingleOrDefaultAsync(cancellationToken);

		if (stored is null)
		{
			return null;
		}

		byte[]? content = await _files.ReadAsync(stored.StorageKey, cancellationToken);

		// A row naming a file the store does not have. Reachable rather than theoretical — a first upload commits
		// its row and a cleanup can fail the other way — so it is answered with the placeholder, which is what an
		// organization without a usable logo should look like. The alternative, a 500, would turn one lost file
		// into a page that will not render.
		return content is null ? null : new OrganizationLogoDto(stored.ContentType, content);
	}
}
