using FluentAssertions;
using Kompaz.Application.Users;
using Kompaz.Application.Users.Commands.UpdateOwnProfile;
using Kompaz.Domain.Enums;
using Kompaz.Presentation.Endpoints;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Users;

[TestFixture]
internal sealed class ProfileTests : ApiTestBase
{
	[Test]
	public async Task AMemberCanRenameThemselvesWithoutAdministratorRights()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAndSignInAsync(administrator, "lid@kompaz.local", "Gewoon Lid", UserRole.Member);

		var response = await member.PutAsJsonAsync("/api/users/me", new UpdateOwnProfileCommand("Hernoemd Lid"), JsonOptions.Web);
		var updated = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		updated!.Name.Should().Be("Hernoemd Lid");
		updated.Email.Should().Be("lid@kompaz.local");
	}

	[Test]
	public async Task TheChangeIsVisibleOnTheNextRead()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAndSignInAsync(administrator, "lid@kompaz.local", "Gewoon Lid", UserRole.Member);

		await member.PutAsJsonAsync("/api/users/me", new UpdateOwnProfileCommand("Hernoemd Lid"), JsonOptions.Web);
		var profile = await member.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);

		profile!.Name.Should().Be("Hernoemd Lid");
	}

	[Test]
	public async Task RenamingYourselfLeavesYourRoleAlone()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAndSignInAsync(administrator, "lid@kompaz.local", "Gewoon Lid", UserRole.Member);

		var response = await member.PutAsJsonAsync("/api/users/me", new UpdateOwnProfileCommand("Hernoemd Lid"), JsonOptions.Web);
		var updated = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions.Web);

		updated!.Role.Should().Be(UserRole.Member);
	}

	[Test]
	public async Task TheProfileEndpointRejectsAnEmptyName()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAndSignInAsync(administrator, "lid@kompaz.local", "Gewoon Lid", UserRole.Member);

		var response = await member.PutAsJsonAsync("/api/users/me", new UpdateOwnProfileCommand("  "), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task TheProfileEndpointRequiresABearerToken()
	{
		var anonymous = CreateClient();

		var response = await anonymous.PutAsJsonAsync("/api/users/me", new UpdateOwnProfileCommand("Niemand"), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task TheProfileEndpointCannotBeUsedToEditSomebodyElse()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "collega@kompaz.local", "Collega");
		var member = await InviteAndSignInAsync(administrator, "lid@kompaz.local", "Gewoon Lid", UserRole.Member);

		await member.PutAsJsonAsync("/api/users/me", new UpdateOwnProfileCommand("Hernoemd Lid"), JsonOptions.Web);
		var other = await administrator.GetFromJsonAsync<UserDto>($"/api/users/{invited.Id}", JsonOptions.Web);

		other!.Name.Should().Be("Collega");
	}

	[Test]
	public async Task AdministratorsStillUseTheIdentifiedEndpointToEditOthers()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "collega@kompaz.local", "Collega");

		var response = await administrator.PutAsJsonAsync(
			$"/api/users/{invited.Id}",
			new UserEndpoints.UpdateUserRequest("Hernoemde Collega", invited.Email, UserRole.Administrator),
			JsonOptions.Web);
		var updated = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		updated!.Name.Should().Be("Hernoemde Collega");
		updated.Role.Should().Be(UserRole.Administrator);
	}
}
