using FluentAssertions;
using Kompaz.Application.Common.Models;
using Kompaz.Application.Organizations;
using Kompaz.Application.Organizations.Commands.CreateOrganization;
using Kompaz.Application.Users;
using Kompaz.Domain.Enums;
using NUnit.Framework;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Users;

[TestFixture]
internal sealed class UserQueryTests : ApiTestBase
{
	[Test]
	public async Task ListingSeparatesInvitedFromActiveUsers()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "wacht-nog@kompaz.local", "Wacht Nog");
		await InviteAndSignInAsync(administrator, "actief@kompaz.local", "Actief Lid", UserRole.Member);

		var invited = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>($"/api/users?status={UserStatus.Invited}", JsonOptions.Web);
		var active = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>($"/api/users?status={UserStatus.Active}", JsonOptions.Web);
		var everyone = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>("/api/users", JsonOptions.Web);

		invited!.Items.Should().OnlyContain(user => user.Status == UserStatus.Invited);
		invited.Items.Should().ContainSingle(user => user.Email == "wacht-nog@kompaz.local");

		active!.Items.Should().OnlyContain(user => user.Status == UserStatus.Active);
		active.Items.Should().Contain(user => user.Email == "actief@kompaz.local");
		active.Items.Should().Contain(user => user.Email == SeededAdministratorEmail);

		everyone!.TotalCount.Should().Be(invited.TotalCount + active.TotalCount);
	}

	[Test]
	public async Task ListingPagesResultsAndReportsTheTotal()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();

		for (int index = 0; index < 7; index++)
		{
			string suffix = index.ToString(CultureInfo.InvariantCulture);
			await InviteAsync(administrator, $"collega{suffix}@kompaz.local", $"Collega {suffix}");
		}

		var first = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>("/api/users?pageNumber=1&pageSize=5", JsonOptions.Web);
		var second = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>("/api/users?pageNumber=2&pageSize=5", JsonOptions.Web);

		first!.TotalCount.Should().Be(8);
		first.Items.Should().HaveCount(5);
		first.PageNumber.Should().Be(1);
		first.TotalPages.Should().Be(2);
		first.HasPreviousPage.Should().BeFalse();
		first.HasNextPage.Should().BeTrue();

		second!.Items.Should().HaveCount(3);
		second.HasPreviousPage.Should().BeTrue();
		second.HasNextPage.Should().BeFalse();

		second.Items.Select(user => user.Id).Should().NotIntersectWith(first.Items.Select(user => user.Id));
	}

	[Test]
	public async Task ListingRejectsAPageLargerThanTheCap()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();

		var response = await administrator.GetAsync($"/api/users?pageSize={PagedQuery.MaximumPageSize + 1}");

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task SearchMatchesTheNameAndTheEmailAddress()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "sanne.jansen@kompaz.local", "Sanne Jansen");
		await InviteAsync(administrator, "pieter.devries@kompaz.local", "Pieter de Vries");

		var byName = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>("/api/users?search=jansen", JsonOptions.Web);
		var byEmail = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>("/api/users?search=pieter.devries", JsonOptions.Web);

		byName!.Items.Should().ContainSingle(user => user.Email == "sanne.jansen@kompaz.local");
		byEmail!.Items.Should().ContainSingle(user => user.Email == "pieter.devries@kompaz.local");
	}

	[Test]
	public async Task AdministratorsOnlySeeTheirOwnOrganization()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();

		var created = await platformAdministrator.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var other = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);
		await InviteAsync(platformAdministrator, "klant@kompaz.local", "Klant Beheerder", UserRole.Administrator, other!.Id);

		var administrator = await SignInAsync("klant@kompaz.local");
		var visible = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>("/api/users", JsonOptions.Web);

		visible!.Items.Should().OnlyContain(user => user.OrganizationId == other.Id);
		visible.Items.Should().NotContain(user => user.Email == SeededAdministratorEmail);
	}

	[Test]
	public async Task PlatformAdministratorsSeeEveryOrganizationUnlessTheyNameOne()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();

		var created = await platformAdministrator.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var other = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);
		await InviteAsync(platformAdministrator, "klant@kompaz.local", "Klant Beheerder", UserRole.Administrator, other!.Id);

		var everyone = await platformAdministrator.GetFromJsonAsync<PaginatedList<UserDto>>("/api/users", JsonOptions.Web);
		var scoped = await platformAdministrator.GetFromJsonAsync<PaginatedList<UserDto>>($"/api/users?organizationId={other.Id}", JsonOptions.Web);

		everyone!.TotalCount.Should().Be(2);
		scoped!.TotalCount.Should().Be(1);
		scoped.Items.Should().ContainSingle(user => user.Email == "klant@kompaz.local");
	}

	[Test]
	public async Task MembersMayNotListUsers()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAndSignInAsync(administrator, "lid@kompaz.local", "Gewoon Lid", UserRole.Member);

		var response = await member.GetAsync("/api/users");

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}
}
