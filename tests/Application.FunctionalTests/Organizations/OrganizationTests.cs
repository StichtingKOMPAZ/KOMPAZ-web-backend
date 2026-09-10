using FluentAssertions;
using Kompaz.Application.Common.Models;
using Kompaz.Application.Organizations;
using Kompaz.Application.Organizations.Commands.CreateOrganization;
using Kompaz.Domain.Enums;
using Kompaz.Presentation.Endpoints;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Organizations;

[TestFixture]
internal sealed class OrganizationTests : ApiTestBase
{
	[Test]
	public async Task PlatformAdministratorsCanWalkTheFullLifecycle()
	{
		var client = await SignInAsPlatformAdministratorAsync();

		var created = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var organization = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);

		created.StatusCode.Should().Be(HttpStatusCode.Created);
		organization!.Name.Should().Be("Klant B.V.");
		organization.UserCount.Should().Be(0);

		var renamed = await client.PutAsJsonAsync(
			$"/api/organizations/{organization.Id}",
			new OrganizationEndpoints.UpdateOrganizationRequest("Klant Holding B.V."), JsonOptions.Web);
		var updated = await renamed.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);

		renamed.StatusCode.Should().Be(HttpStatusCode.OK);
		updated!.Name.Should().Be("Klant Holding B.V.");
		updated.UpdatedUtc.Should().BeOnOrAfter(organization.UpdatedUtc);

		var deleted = await client.DeleteAsync($"/api/organizations/{organization.Id}");
		var readBack = await client.GetAsync($"/api/organizations/{organization.Id}");

		deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
		readBack.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	/// <summary>
	/// The flag says which organization runs the platform, and the front end needs it to know where a super admin
	/// may be placed. Nothing over the API sets it, so a tenant created here never comes back flagged.
	/// </summary>
	[Test]
	public async Task ExactlyOneOrganizationRunsThePlatformAndItIsTheSeededOne()
	{
		var client = await SignInAsPlatformAdministratorAsync();

		var created = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var tenant = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);
		var everyone = await client.GetFromJsonAsync<PaginatedList<OrganizationDto>>("/api/organizations", JsonOptions.Web);

		tenant!.IsPlatform.Should().BeFalse();
		everyone!.Items.Should().ContainSingle(organization => organization.IsPlatform)
			.Which.Name.Should().Be("Stichting KOMPAZ");
	}

	[Test]
	public async Task CreatingADuplicateNameConflicts()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);

		var response = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task DeletingAnOrganizationRemovesItsUsers()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var created = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var organization = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);
		var member = await InviteAsync(client, "klant@kompaz.local", "Klant Lid", UserRole.Member, organization!.Id);

		await client.DeleteAsync($"/api/organizations/{organization.Id}");
		var readBack = await client.GetAsync($"/api/users/{member.Id}");

		readBack.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task ListingCountsInvitedAndActiveUsersSeparately()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(client, "wacht-nog@kompaz.local", "Wacht Nog");

		var page = await client.GetFromJsonAsync<PaginatedList<OrganizationDto>>("/api/organizations?search=KOMPAZ", JsonOptions.Web);
		var platformOrganization = page!.Items.Should().ContainSingle().Subject;

		platformOrganization.UserCount.Should().Be(2);
		platformOrganization.ActiveUserCount.Should().Be(1);
		platformOrganization.InvitedUserCount.Should().Be(1);
	}

	[Test]
	public async Task ListingPagesResults()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Alfa"), JsonOptions.Web);
		await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Bravo"), JsonOptions.Web);

		var page = await client.GetFromJsonAsync<PaginatedList<OrganizationDto>>("/api/organizations?pageNumber=1&pageSize=2", JsonOptions.Web);

		page!.TotalCount.Should().Be(3);
		page.Items.Should().HaveCount(2);
		page.TotalPages.Should().Be(2);
		page.HasNextPage.Should().BeTrue();
	}

	[Test]
	public async Task AdministratorsOnlySeeAndRenameTheirOwnOrganization()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var created = await platformAdministrator.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var other = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);

		var administrator = await InviteAndSignInAsync(platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);
		var me = await administrator.GetFromJsonAsync<Kompaz.Application.Users.UserDto>("/api/auth/me", JsonOptions.Web);

		var visible = await administrator.GetFromJsonAsync<PaginatedList<OrganizationDto>>("/api/organizations", JsonOptions.Web);
		var renameOwn = await administrator.PutAsJsonAsync(
			$"/api/organizations/{me!.OrganizationId}",
			new OrganizationEndpoints.UpdateOrganizationRequest("Eigen Organisatie"), JsonOptions.Web);
		var renameOther = await administrator.PutAsJsonAsync(
			$"/api/organizations/{other!.Id}",
			new OrganizationEndpoints.UpdateOrganizationRequest("Gekaapt"), JsonOptions.Web);

		visible!.Items.Should().ContainSingle(organization => organization.Id == me.OrganizationId);
		renameOwn.StatusCode.Should().Be(HttpStatusCode.OK);
		renameOther.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task AdministratorsMayNotCreateOrDeleteOrganizations()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var administrator = await InviteAndSignInAsync(platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);
		var me = await administrator.GetFromJsonAsync<Kompaz.Application.Users.UserDto>("/api/auth/me", JsonOptions.Web);

		var create = await administrator.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Eigen B.V."), JsonOptions.Web);
		var delete = await administrator.DeleteAsync($"/api/organizations/{me!.OrganizationId}");

		create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task ReadingRequiresABearerToken()
	{
		var client = CreateClient();

		var response = await client.GetAsync("/api/organizations");

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}
}
