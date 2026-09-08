using FluentAssertions;
using Kompaz.Application.Common.Models;
using Kompaz.Application.Users;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Common;

/// <summary>
/// A client picks both the page number and the page size, so the product of the two is attacker-chosen. These pin
/// the arithmetic that turns them into an offset.
/// </summary>
[TestFixture]
internal sealed class PagingBoundsTests : ApiTestBase
{
	[Test]
	public async Task APageBeyondTheEndIsEmpty()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();

		var page = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>(
			"/api/users?pageNumber=5&pageSize=2", JsonOptions.Web);

		page!.Items.Should().BeEmpty();
	}

	/// <summary>
	/// (pageNumber - 1) * pageSize is 4294967292 here, which wraps to -4 in 32 bits. A negative offset is one SQLite
	/// silently reads as zero, so the overflow used to serve the first page under a page number nowhere near it.
	/// </summary>
	[TestCase(2147483647, 2)]
	[TestCase(1073741825, 4)]
	[TestCase(int.MaxValue, 100)]
	public async Task APageNumberThatOverflowsTheOffsetIsStillBeyondTheEnd(int pageNumber, int pageSize)
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "een@kompaz.local", "Een");
		await InviteAsync(administrator, "twee@kompaz.local", "Twee");

		var response = await administrator.GetAsync(
			new Uri($"/api/users?pageNumber={pageNumber}&pageSize={pageSize}", UriKind.Relative));
		var page = await response.Content.ReadFromJsonAsync<PaginatedList<UserDto>>(JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		page!.Items.Should().BeEmpty();
		page.TotalCount.Should().Be(3);
	}

	[Test]
	public async Task TheSameHoldsForOrganizations()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();

		var page = await administrator.GetFromJsonAsync<PaginatedList<Kompaz.Application.Organizations.OrganizationDto>>(
			$"/api/organizations?pageNumber={int.MaxValue}&pageSize=100", JsonOptions.Web);

		page!.Items.Should().BeEmpty();
		page.TotalCount.Should().Be(1);
	}
}
