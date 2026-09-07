namespace Kompaz.Application.Common.Models;

/// <summary>
/// Enforces the paging bounds for any list query so a client cannot ask for an unbounded page.
/// </summary>
/// <typeparam name="TQuery">The list query being validated.</typeparam>
public abstract class PagedQueryValidator<TQuery> : AbstractValidator<TQuery>
	where TQuery : PagedQuery
{
	protected PagedQueryValidator()
	{
		RuleFor(query => query.PageNumber)
			.GreaterThanOrEqualTo(1);

		RuleFor(query => query.PageSize)
			.InclusiveBetween(1, PagedQuery.MaximumPageSize);
	}
}
