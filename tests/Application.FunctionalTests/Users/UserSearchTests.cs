using FluentAssertions;
using Kompaz.Application.Common.Models;
using Kompaz.Application.Organizations;
using Kompaz.Application.Users;
using NUnit.Framework;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Users;

/// <summary>
/// The <c>search</c> parameter is documented as case-insensitive, and PostgreSQL's <c>LIKE</c> ignores no case on
/// its own, so that only holds because both sides are folded. These pin it, accents included.
/// </summary>
[TestFixture]
internal sealed class UserSearchTests : ApiTestBase
{
	[TestCase("jansen")]
	[TestCase("JANSEN")]
	[TestCase("JaNsEn")]
	public async Task ANameIsFoundWhateverTheCase(string term)
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "pj@kompaz.local", "Pieter Jansen");

		var page = await SearchUsersAsync(administrator, term);

		page.Items.Should().ContainSingle().Which.Name.Should().Be("Pieter Jansen");
	}

	/// <summary>
	/// The reason folding happens through <c>upper()</c> on both sides rather than in .NET on one of them: the two
	/// agree about é, so a lower-case accented search still finds the accented name.
	/// </summary>
	[TestCase("renée")]
	[TestCase("RENÉE")]
	[TestCase("Renée")]
	public async Task AnAccentedNameIsFoundWhateverTheCase(string term)
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "rd@kompaz.local", "Renée de Vries");

		var page = await SearchUsersAsync(administrator, term);

		page.Items.Should().ContainSingle().Which.Name.Should().Be("Renée de Vries");
	}

	[TestCase("PIETER@KOMPAZ.LOCAL")]
	[TestCase("pieter@kompaz")]
	public async Task AnEmailAddressIsFoundWhateverTheCase(string term)
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "pieter@kompaz.local", "Pieter Jansen");

		var page = await SearchUsersAsync(administrator, term);

		page.Items.Should().ContainSingle().Which.Email.Should().Be("pieter@kompaz.local");
	}

	[Test]
	public async Task OrganizationsAreSearchedTheSameWay()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await administrator.PostAsJsonAsync("/api/organizations", new { name = "Bouwbedrijf Müller" }, JsonOptions.Web);

		var page = await administrator.GetFromJsonAsync<PaginatedList<OrganizationDto>>(
			"/api/organizations?search=müller", JsonOptions.Web);

		page!.Items.Should().ContainSingle().Which.Name.Should().Be("Bouwbedrijf Müller");
	}

	/// <summary>
	/// A wildcard the user typed is a literal, not a wildcard, or a search for "%" would return everybody.
	/// </summary>
	[Test]
	public async Task AWildcardIsSearchedForLiterally()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "odd@kompaz.local", "100% Zeker");
		await InviteAsync(administrator, "plain@kompaz.local", "Gewoon Iemand");

		var page = await SearchUsersAsync(administrator, "100%");

		page.Items.Should().ContainSingle().Which.Name.Should().Be("100% Zeker");
	}

	[Test]
	public async Task AnUnderscoreIsSearchedForLiterally()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "us@kompaz.local", "Jan_Piet");
		await InviteAsync(administrator, "other@kompaz.local", "JanXPiet");

		var page = await SearchUsersAsync(administrator, "jan_piet");

		page.Items.Should().ContainSingle().Which.Name.Should().Be("Jan_Piet");
	}

	private static async Task<PaginatedList<UserDto>> SearchUsersAsync(HttpClient client, string term)
	{
		var page = await client.GetFromJsonAsync<PaginatedList<UserDto>>(
			new Uri($"/api/users?search={Uri.EscapeDataString(term)}", UriKind.Relative), JsonOptions.Web);

		return page!;
	}
}
