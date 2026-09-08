using FluentAssertions;
using Kompaz.Application.Organizations;
using Kompaz.Application.Organizations.Commands.CreateOrganization;
using Kompaz.Application.Users;
using Kompaz.Application.Users.Commands.InviteUser;
using Kompaz.Domain.Enums;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Users;

[TestFixture]
internal sealed class UserInvitationTests : ApiTestBase
{
	[Test]
	public async Task InvitingSomeoneCreatesAnInvitedUserAndSendsALink()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();

		var invited = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");

		invited.Status.Should().Be(UserStatus.Invited);
		invited.Role.Should().Be(UserRole.Member);
		invited.InvitedUtc.Should().NotBeNull();
		invited.ActivatedUtc.Should().BeNull();
		Emails.WasSentTo("nieuw@kompaz.local").Should().BeTrue();
	}

	[Test]
	public async Task AcceptingTheInvitationActivatesTheUser()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");

		var invitee = await SignInAsync("nieuw@kompaz.local");
		var profile = await invitee.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);

		profile.Should().NotBeNull();
		profile!.Id.Should().Be(invited.Id);
		profile.Status.Should().Be(UserStatus.Active);
		profile.ActivatedUtc.Should().NotBeNull();
	}

	[Test]
	public async Task InvitingAnAddressThatAlreadyHasAnAccountConflicts()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();

		var response = await administrator.PostAsJsonAsync(
			"/api/users/invitations",
			new InviteUserCommand(SeededAdministratorEmail, "Duplicate"), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task ResendingAnInvitationIssuesANewLinkAndRetiresTheOld()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");
		string first = Emails.TokenFor("nieuw@kompaz.local");

		var response = await administrator.PostAsync($"/api/users/{invited.Id}/invitations", content: null);
		string second = Emails.TokenFor("nieuw@kompaz.local");

		response.StatusCode.Should().Be(HttpStatusCode.Accepted);
		second.Should().NotBe(first);

		var anonymous = CreateClient();
		var withOldLink = await anonymous.PostAsJsonAsync(
			"/api/auth/tokens",
			new Kompaz.Application.Authentication.Commands.RedeemLoginToken.RedeemLoginTokenCommand(first), JsonOptions.Web);

		withOldLink.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task ResendingAnInvitationToAnActiveUserConflicts()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");
		await SignInAsync("nieuw@kompaz.local");

		var response = await administrator.PostAsync($"/api/users/{invited.Id}/invitations", content: null);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task ReinvitingSomebodyWhoHasNotAcceptedYetSendsAFreshLink()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var first = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");
		string firstToken = Emails.TokenFor("nieuw@kompaz.local");

		var second = await InviteAsync(administrator, "nieuw@kompaz.local", "Betere Naam", UserRole.Administrator);
		string secondToken = Emails.TokenFor("nieuw@kompaz.local");

		second.Id.Should().Be(first.Id);
		second.Name.Should().Be("Betere Naam");
		second.Role.Should().Be(UserRole.Administrator);
		secondToken.Should().NotBe(firstToken);
	}

	[Test]
	public async Task ReinvitingRetiresTheLinkSentBefore()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");
		string firstToken = Emails.TokenFor("nieuw@kompaz.local");

		await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");

		var withOldLink = await CreateClient().PostAsJsonAsync(
			"/api/auth/tokens", new { token = firstToken }, JsonOptions.Web);

		withOldLink.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task AskingForAMagicLinkLeavesAPendingInvitationAlone()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");
		string invitation = Emails.TokenFor("nieuw@kompaz.local");

		// Anonymous, and repeatable by anybody who knows the address. It must not retire the invitation.
		var anonymous = CreateClient();
		for (int attempt = 0; attempt < 3; attempt++)
		{
			var requested = await anonymous.PostAsJsonAsync(
				"/api/auth/magic-link", new { email = "nieuw@kompaz.local" }, JsonOptions.Web);
			requested.StatusCode.Should().Be(HttpStatusCode.Accepted);
		}

		Emails.TokenFor("nieuw@kompaz.local").Should().NotBe(invitation);

		var withInvitation = await CreateClient().PostAsJsonAsync(
			"/api/auth/tokens", new { token = invitation }, JsonOptions.Web);

		withInvitation.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Test]
	public async Task AdministratorsMayNotResendAPlatformAdministratorsInvitation()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var pending = await InviteAsync(platformAdministrator, "root@kompaz.local", "Root", UserRole.PlatformAdministrator);
		var administrator = await InviteAndSignInAsync(platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var response = await administrator.PostAsync($"/api/users/{pending.Id}/invitations", content: null);

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task AdministratorsMayNotReinviteAPlatformAdministrator()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(platformAdministrator, "root@kompaz.local", "Root", UserRole.PlatformAdministrator);
		var administrator = await InviteAndSignInAsync(platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var response = await administrator.PostAsJsonAsync(
			"/api/users/invitations",
			new InviteUserCommand("root@kompaz.local", "Root", UserRole.Member), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task MembersMayNotInviteAnyone()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAndSignInAsync(administrator, "lid@kompaz.local", "Gewoon Lid", UserRole.Member);

		var response = await member.PostAsJsonAsync(
			"/api/users/invitations",
			new InviteUserCommand("nog-iemand@kompaz.local", "Nog Iemand"), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task AdministratorsMayNotGrantThePlatformAdministratorRole()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var administrator = await InviteAndSignInAsync(platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var response = await administrator.PostAsJsonAsync(
			"/api/users/invitations",
			new InviteUserCommand("root@kompaz.local", "Root", UserRole.PlatformAdministrator), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task AdministratorsMayNotInviteIntoAnotherOrganization()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();

		var created = await platformAdministrator.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Andere Organisatie"), JsonOptions.Web);
		var other = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);

		var administrator = await InviteAndSignInAsync(platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var response = await administrator.PostAsJsonAsync(
			"/api/users/invitations",
			new InviteUserCommand("buiten@kompaz.local", "Buitenstaander", UserRole.Member, other!.Id), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}
}
