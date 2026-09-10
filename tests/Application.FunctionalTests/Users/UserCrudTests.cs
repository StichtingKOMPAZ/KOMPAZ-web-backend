using FluentAssertions;
using Kompaz.Application.Organizations;
using Kompaz.Application.Organizations.Commands.CreateOrganization;
using Kompaz.Application.Users;
using Kompaz.Domain.Enums;
using Kompaz.Presentation.Endpoints;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Users;

[TestFixture]
internal sealed class UserCrudTests : ApiTestBase
{
	[Test]
	public async Task AUserCanBeReadBackById()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");

		var user = await administrator.GetFromJsonAsync<UserDto>($"/api/users/{invited.Id}", JsonOptions.Web);

		user.Should().NotBeNull();
		user!.Email.Should().Be("nieuw@kompaz.local");
		user.OrganizationName.Should().NotBeNullOrWhiteSpace();
	}

	[Test]
	public async Task ReadingAnUnknownUserReturnsNotFound()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();

		var response = await administrator.GetAsync($"/api/users/{Guid.NewGuid()}");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task UpdatingChangesTheNameAndRole()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");

		var response = await administrator.PutAsJsonAsync(
			$"/api/users/{invited.Id}",
			new UserEndpoints.UpdateUserRequest("Hernoemde Collega", UserRole.Administrator), JsonOptions.Web);
		var updated = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		updated!.Name.Should().Be("Hernoemde Collega");
		updated.Role.Should().Be(UserRole.Administrator);
		updated.Email.Should().Be("nieuw@kompaz.local");
	}

	[Test]
	public async Task UpdatingRejectsAnEmptyName()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");

		var response = await administrator.PutAsJsonAsync(
			$"/api/users/{invited.Id}",
			new UserEndpoints.UpdateUserRequest(" ", UserRole.Member), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task DeletingRemovesTheUser()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");

		var deleted = await administrator.DeleteAsync($"/api/users/{invited.Id}");
		var readBack = await administrator.GetAsync($"/api/users/{invited.Id}");

		deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
		readBack.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task DeletingYourOwnAccountIsRefused()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var me = await administrator.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);

		var response = await administrator.DeleteAsync($"/api/users/{me!.Id}");

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task MembersMayReadThemselvesButNotChangeAnyone()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAndSignInAsync(administrator, "lid@kompaz.local", "Gewoon Lid", UserRole.Member);
		var me = await member.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);

		var read = await member.GetAsync($"/api/users/{me!.Id}");
		var write = await member.PutAsJsonAsync(
			$"/api/users/{me.Id}",
			new UserEndpoints.UpdateUserRequest("Andere Naam", UserRole.Member), JsonOptions.Web);

		read.StatusCode.Should().Be(HttpStatusCode.OK);
		write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task MembersMayNotReadTheirColleagues()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var colleague = await InviteAsync(administrator, "collega@kompaz.local", "Collega");
		var member = await InviteAndSignInAsync(administrator, "lid@kompaz.local", "Gewoon Lid", UserRole.Member);

		var response = await member.GetAsync($"/api/users/{colleague.Id}");

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task TheLastPlatformAdministratorMayNotGiveUpTheRole()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var me = await platformAdministrator.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);

		var response = await platformAdministrator.PutAsJsonAsync(
			$"/api/users/{me!.Id}",
			new UserEndpoints.UpdateUserRequest(me.Name, UserRole.Administrator), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task APlatformAdministratorMayStepDownOnceAnotherIsAppointed()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var me = await platformAdministrator.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);
		await InviteAsync(platformAdministrator, "opvolger@kompaz.local", "Opvolger", UserRole.PlatformAdministrator);

		var response = await platformAdministrator.PutAsJsonAsync(
			$"/api/users/{me!.Id}",
			new UserEndpoints.UpdateUserRequest(me.Name, UserRole.Administrator), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// The edit is the other way to hand out a role, so it answers to the same restriction the invitation does.
	/// Otherwise an administrator would invite a member and promote them a moment later.
	/// </summary>
	[Test]
	public async Task AdministratorsMayNotPromoteAMemberToAdministrator()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(platformAdministrator, "lid@kompaz.local", "Gewoon Lid");
		var administrator = await InviteAndSignInAsync(platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var promote = await administrator.PutAsJsonAsync(
			$"/api/users/{member.Id}",
			new UserEndpoints.UpdateUserRequest(member.Name, UserRole.Administrator), JsonOptions.Web);
		var rename = await administrator.PutAsJsonAsync(
			$"/api/users/{member.Id}",
			new UserEndpoints.UpdateUserRequest("Andere Naam", UserRole.Member), JsonOptions.Web);

		promote.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		rename.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// The role does not travel to a tenant, so a promotion cannot smuggle it there either.
	/// </summary>
	[Test]
	public async Task NobodyInATenantOrganizationCanBePromotedToPlatformAdministrator()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var created = await platformAdministrator.PostAsJsonAsync(
			"/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var tenant = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);
		var member = await InviteAsync(platformAdministrator, "lid@klant.local", "Klant Lid", UserRole.Member, tenant!.Id);

		var response = await platformAdministrator.PutAsJsonAsync(
			$"/api/users/{member.Id}",
			new UserEndpoints.UpdateUserRequest(member.Name, UserRole.PlatformAdministrator), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task AdministratorsMayNotRenameAPlatformAdministrator()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var me = await platformAdministrator.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);
		var administrator = await InviteAndSignInAsync(platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		// The role is left alone, so only the unconditional guard stands between them and editing their superior.
		var response = await administrator.PutAsJsonAsync(
			$"/api/users/{me!.Id}",
			new UserEndpoints.UpdateUserRequest("Andere Naam", UserRole.PlatformAdministrator), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task AdministratorsMayNotDeleteAPlatformAdministrator()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var me = await platformAdministrator.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);
		var administrator = await InviteAndSignInAsync(platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var response = await administrator.DeleteAsync($"/api/users/{me!.Id}");

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}
}
