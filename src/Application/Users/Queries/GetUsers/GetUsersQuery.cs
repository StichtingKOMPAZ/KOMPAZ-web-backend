using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Models;
using Kompaz.Application.Common.Search;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Users.Queries.GetUsers;

/// <summary>
/// Returns a page of users, optionally narrowed to those still invited or already active.
/// Administrators see their own organization; platform administrators see every organization unless they name one.
/// </summary>
[Authorize(MinimumRole = UserRole.Administrator)]
public record GetUsersQuery : PagedQuery, IRequest<PaginatedList<UserDto>>
{
	/// <summary>
	/// Gets the lifecycle stage to filter on, or <see langword="null"/> for both invited and active users.
	/// </summary>
	public UserStatus? Status { get; init; }

	/// <summary>
	/// Gets an optional case-insensitive fragment matched against the name and the email address.
	/// </summary>
	public string? Search { get; init; }

	/// <summary>
	/// Gets the organization to list. Defaults to the caller's own organization.
	/// </summary>
	public Guid? OrganizationId { get; init; }
}

public class GetUsersQueryValidator : PagedQueryValidator<GetUsersQuery>
{
	public GetUsersQueryValidator()
	{
		RuleFor(query => query.Status)
			.IsInEnum()
			.When(query => query.Status.HasValue);

		RuleFor(query => query.Search)
			.MaximumLength(320);

		RuleFor(query => query.OrganizationId)
			.NotEqual(Guid.Empty)
			.When(query => query.OrganizationId.HasValue);
	}
}

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, PaginatedList<UserDto>>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _currentUser;

	public GetUsersQueryHandler(IApplicationDbContext context, IUser currentUser)
	{
		_context = context;
		_currentUser = currentUser;
	}

	public Task<PaginatedList<UserDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
	{
		IQueryable<User> query = _context.Users.AsNoTracking();

		if (request.OrganizationId is { } requestedOrganizationId)
		{
			OrganizationAccess.EnsureCanRead(_currentUser, requestedOrganizationId);
			query = query.Where(user => user.OrganizationId == requestedOrganizationId);
		}
		else if (!OrganizationAccess.IsPlatformAdministrator(_currentUser))
		{
			var organizationId = OrganizationAccess.ResolveTarget(_currentUser, null);
			query = query.Where(user => user.OrganizationId == organizationId);
		}

		if (request.Status is { } status)
		{
			query = query.Where(user => user.Status == status);
		}

		if (!string.IsNullOrWhiteSpace(request.Search))
		{
			string pattern = SearchPattern.Contains(request.Search.Trim());

			// NormalizedEmail is already upper-cased, which is what it is for; the name has to be folded here.
			query = query.Where(user =>
				EF.Functions.Like(user.Name.ToUpper(), pattern, SearchPattern.EscapeCharacter)
				|| EF.Functions.Like(user.NormalizedEmail, pattern, SearchPattern.EscapeCharacter));
		}

		return PaginatedList<UserDto>.CreateAsync(
			query
				.OrderBy(user => user.Name)
				.ThenBy(user => user.Id)
				.Select(UserDto.Projection),
			request.PageNumber,
			request.PageSize,
			cancellationToken);
	}
}
