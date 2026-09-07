namespace Kompaz.Application.Common.Models;

/// <summary>
/// One page of results together with the counters a client needs to render pagination controls.
/// </summary>
/// <typeparam name="T">The item type on the page.</typeparam>
public sealed record PaginatedList<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount)
{
	public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

	public bool HasPreviousPage => PageNumber > 1;

	public bool HasNextPage => PageNumber < TotalPages;

	/// <summary>
	/// Counts the source query and materializes the requested page from it.
	/// </summary>
	public static async Task<PaginatedList<T>> CreateAsync(IQueryable<T> source, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		int totalCount = await source.CountAsync(cancellationToken);

		var items = await source
			.Skip((pageNumber - 1) * pageSize)
			.Take(pageSize)
			.ToListAsync(cancellationToken);

		return new PaginatedList<T>(items, pageNumber, pageSize, totalCount);
	}
}
