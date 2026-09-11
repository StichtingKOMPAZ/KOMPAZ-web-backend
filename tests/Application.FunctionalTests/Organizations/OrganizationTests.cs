using FluentAssertions;
using Kompaz.Application.Common.Models;
using Kompaz.Application.Organizations;
using Kompaz.Application.Organizations.Commands.CreateOrganization;
using Kompaz.Domain.Enums;
using Kompaz.Presentation.Endpoints;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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

	/// <summary>
	/// Unique on the platform means unique to a person reading the list, so two organizations may not differ by
	/// case alone.
	/// </summary>
	[Test]
	public async Task ANameThatDiffersOnlyByCaseIsStillADuplicate()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Elkerliek"), JsonOptions.Web);

		var response = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("elkerliek"), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	/// <summary>
	/// Surrounding space is not a second name either, and the name that comes back is the trimmed one.
	/// </summary>
	[Test]
	public async Task ANameIsTrimmedBeforeItIsComparedAndStored()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var created = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("  Elkerliek  "), JsonOptions.Web);
		var organization = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);

		var again = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Elkerliek"), JsonOptions.Web);

		organization!.Name.Should().Be("Elkerliek");
		again.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	/// <summary>
	/// The wording is the ticket's, so it is asserted rather than left to whoever edits the validator next. A
	/// field error, not just a status: the dialog puts it under "Naam organisatie".
	/// </summary>
	[TestCase("")]
	[TestCase("   ")]
	public async Task AnOrganizationNeedsAName(string name)
	{
		var client = await SignInAsPlatformAdministratorAsync();

		var response = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand(name), JsonOptions.Web);
		var problem = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		problem.GetProperty("errors").GetProperty("Name").EnumerateArray()
			.Select(error => error.GetString())
			.Should().Contain(OrganizationMessages.NameRequired);
	}

	/// <summary>
	/// The other sentence the ticket dictates. Both duplicate paths answer with it — the pre-check here, and the
	/// unique index when two requests race past it — so a caller cannot be told two different things about the
	/// same clash.
	/// </summary>
	[Test]
	public async Task ADuplicateNameIsReportedInTheTicketsWords()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Elkerliek"), JsonOptions.Web);

		var response = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Elkerliek"), JsonOptions.Web);
		var problem = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		problem.GetProperty("detail").GetString().Should().Be(OrganizationMessages.NameTaken);
	}

	/// <summary>
	/// Every platform administrator belongs to it, so nobody can reach this today — which is exactly why it is
	/// stated rather than left to follow from where the role is held.
	/// </summary>
	[Test]
	public async Task TheOrganizationThatRunsThePlatformCannotBeDeleted()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var everyone = await client.GetFromJsonAsync<PaginatedList<OrganizationDto>>("/api/organizations", JsonOptions.Web);
		var platform = everyone!.Items.Single(organization => organization.IsPlatform);

		var response = await client.DeleteAsync($"/api/organizations/{platform.Id}");

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

	/// <summary>
	/// The accounts go with the organization, so the people who had them are told the same thing they would be
	/// told if an administrator had deleted them one at a time.
	/// </summary>
	[Test]
	public async Task DeletingAnOrganizationTellsItsUsersTheirAccountIsGone()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var created = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var organization = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);
		await InviteAsync(client, "klant@kompaz.local", "Klant Lid", UserRole.Member, organization!.Id);
		await InviteAsync(client, "tweede@kompaz.local", "Tweede Lid", UserRole.Member, organization.Id);

		await client.DeleteAsync($"/api/organizations/{organization.Id}");

		Emails.ToldAboutDeletion("klant@kompaz.local").Should().BeTrue();
		Emails.ToldAboutDeletion("tweede@kompaz.local").Should().BeTrue();
		Emails.ToldAboutDeletion(SeededAdministratorEmail).Should().BeFalse();
	}

	/// <summary>
	/// The notice goes out after the deletion is committed, so a relay that is down costs the notice and not the
	/// deletion — the administrator's obvious response to a 500 would be to delete again and get a 404.
	/// </summary>
	[Test]
	public async Task ARelayThatIsDownDoesNotUndoTheDeletion()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var created = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var organization = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);
		await InviteAsync(client, "klant@kompaz.local", "Klant Lid", UserRole.Member, organization!.Id);

		Emails.DeliveryFails = true;
		var deleted = await client.DeleteAsync($"/api/organizations/{organization.Id}");

		deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await client.GetAsync($"/api/organizations/{organization.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
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

	/// <summary>
	/// The count on a row and the roster the row expands into have to agree, so a deleted user is in neither.
	/// </summary>
	[Test]
	public async Task DeletedUsersAreNotCounted()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(client, "vertrokken@kompaz.local", "Vertrokken Lid");

		var before = await FindPlatformAsync(client);
		await client.DeleteAsync($"/api/users/{member.Id}");
		var after = await FindPlatformAsync(client);

		before.UserCount.Should().Be(2);
		before.InvitedUserCount.Should().Be(1);
		after.UserCount.Should().Be(1);
		after.InvitedUserCount.Should().Be(0);
		after.ActiveUserCount.Should().Be(1);
	}

	/// <summary>
	/// What the list is for: every organization, with the name, the count and where to fetch the logo, in one
	/// request rather than one per row.
	/// </summary>
	[Test]
	public async Task ListingCarriesWhatARowShows()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var created = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Elkerliek"), JsonOptions.Web);
		var organization = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);
		await InviteAsync(client, "klant@kompaz.local", "Klant Lid", UserRole.Member, organization!.Id);

		var page = await client.GetFromJsonAsync<PaginatedList<OrganizationDto>>("/api/organizations", JsonOptions.Web);
		var row = page!.Items.Single(candidate => candidate.Id == organization.Id);

		row.Name.Should().Be("Elkerliek");
		row.UserCount.Should().Be(1);
		row.HasLogo.Should().BeFalse();
		row.LogoUrl.Should().Be($"/api/organizations/{organization.Id}/logo");
	}

	/// <summary>
	/// The other half of an expanded row: who is in the organization. A platform administrator asks for a tenant
	/// they do not belong to, which is the only way this view is ever used.
	/// </summary>
	[Test]
	public async Task ARowCanBeExpandedIntoTheUsersOfThatOrganization()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var created = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Elkerliek"), JsonOptions.Web);
		var organization = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);
		await InviteAsync(client, "klant@kompaz.local", "Klant Lid", UserRole.Member, organization!.Id);
		await InviteAsync(client, "tweede@kompaz.local", "Tweede Lid", UserRole.Member, organization.Id);

		var users = await client.GetFromJsonAsync<PaginatedList<Kompaz.Application.Users.UserDto>>(
			$"/api/users?organizationId={organization.Id}", JsonOptions.Web);
		var row = await client.GetFromJsonAsync<OrganizationDto>($"/api/organizations/{organization.Id}", JsonOptions.Web);

		users!.TotalCount.Should().Be(2);
		users.Items.Select(user => user.Email).Should().BeEquivalentTo("klant@kompaz.local", "tweede@kompaz.local");
		row!.UserCount.Should().Be(users.TotalCount);
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

	private static async Task<OrganizationDto> FindPlatformAsync(HttpClient client)
	{
		var page = await client.GetFromJsonAsync<PaginatedList<OrganizationDto>>("/api/organizations", JsonOptions.Web);

		return page!.Items.Single(organization => organization.IsPlatform);
	}
}
