using FluentAssertions;
using Kompaz.Application.Common.Models;
using NUnit.Framework;

namespace Kompaz.Application.UnitTests.Common;

[TestFixture]
internal class PaginatedListTests
{
	[Test]
	public void ShouldDescribeAPageInTheMiddle()
	{
		var page = new PaginatedList<int>([11, 12, 13, 14, 15], PageNumber: 2, PageSize: 5, TotalCount: 12);

		page.TotalPages.Should().Be(3);
		page.HasPreviousPage.Should().BeTrue();
		page.HasNextPage.Should().BeTrue();
	}

	[Test]
	public void ShouldRoundPartialPagesUp()
	{
		var page = new PaginatedList<int>([1], PageNumber: 1, PageSize: 10, TotalCount: 11);

		page.TotalPages.Should().Be(2);
	}

	[Test]
	public void ShouldReportNoNeighboursForASinglePage()
	{
		var page = new PaginatedList<int>([1, 2, 3], PageNumber: 1, PageSize: 25, TotalCount: 3);

		page.TotalPages.Should().Be(1);
		page.HasPreviousPage.Should().BeFalse();
		page.HasNextPage.Should().BeFalse();
	}

	[Test]
	public void ShouldReportNoPagesWhenThereAreNoResults()
	{
		var page = new PaginatedList<int>([], PageNumber: 1, PageSize: 25, TotalCount: 0);

		page.TotalPages.Should().Be(0);
		page.HasPreviousPage.Should().BeFalse();
		page.HasNextPage.Should().BeFalse();
	}
}
