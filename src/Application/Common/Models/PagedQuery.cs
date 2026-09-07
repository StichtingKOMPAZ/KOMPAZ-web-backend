namespace Kompaz.Application.Common.Models;

/// <summary>
/// The paging inputs shared by every list query.
/// </summary>
public abstract record PagedQuery
{
	public const int MaximumPageSize = 100;

	public const int DefaultPageSize = 25;

	public int PageNumber { get; init; } = 1;

	public int PageSize { get; init; } = DefaultPageSize;
}
