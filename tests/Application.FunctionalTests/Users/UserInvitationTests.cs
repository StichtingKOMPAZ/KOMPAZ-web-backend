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

	/// <summary>
	/// An administrator runs their own organization but does not staff it with peers: the role they may hand out is
	/// the instructor's, and appointing another administrator is the platform's call.
	/// </summary>
	[Test]
	public async Task AdministratorsMayOnlyInviteMembers()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var administrator = await InviteAndSignInAsync(platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var member = await administrator.PostAsJsonAsync(
			"/api/users/invitations",
			new InviteUserCommand("instructeur@kompaz.local", "Instructeur", UserRole.Member), JsonOptions.Web);
		var peer = await administrator.PostAsJsonAsync(
			"/api/users/invitations",
			new InviteUserCommand("mede-beheer@kompaz.local", "Mede Beheerder", UserRole.Administrator), JsonOptions.Web);

		member.StatusCode.Should().Be(HttpStatusCode.Created);
		peer.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	/// <summary>
	/// Only name and email are needed: the organization defaults to the administrator's own and the role to the
	/// only one they may grant, which is the modal the front end shows them.
	/// </summary>
	[Test]
	public async Task AnAdministratorNeedOnlySupplyANameAndAnAddress()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var me = await platformAdministrator.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);
		var administrator = await InviteAndSignInAsync(platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var response = await administrator.PostAsJsonAsync(
			"/api/users/invitations",
			new { email = "instructeur@kompaz.local", name = "Instructeur" }, JsonOptions.Web);
		var invited = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Created);
		invited!.Role.Should().Be(UserRole.Member);
		invited.OrganizationId.Should().Be(me!.OrganizationId);
	}

	/// <summary>
	/// Platform administration is a job at the organization that runs the platform, so the role does not travel to
	/// a tenant even when the caller is entitled to grant it.
	/// </summary>
	[Test]
	public async Task APlatformAdministratorMayNotBeInvitedIntoATenantOrganization()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var created = await platformAdministrator.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var tenant = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);

		var response = await platformAdministrator.PostAsJsonAsync(
			"/api/users/invitations",
			new InviteUserCommand("root@klant.local", "Root", UserRole.PlatformAdministrator, tenant!.Id), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task APlatformAdministratorMayBeInvitedIntoThePlatformOrganization()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var me = await platformAdministrator.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);

		var invited = await InviteAsync(platformAdministrator, "root@kompaz.local", "Root", UserRole.PlatformAdministrator, me!.OrganizationId);

		invited.Role.Should().Be(UserRole.PlatformAdministrator);
	}

	/// <summary>
	/// An invitee who asks for a magic link of their own is activated by it, so the invitation has been accepted and
	/// the week-long link it sent must not stay redeemable behind them.
	/// </summary>
	[Test]
	public async Task SigningInAnyOtherWayStillSpendsTheInvitation()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");
		string invitation = Emails.TokenFor("nieuw@kompaz.local");

		await SignInAsync("nieuw@kompaz.local");

		var withTheInvitation = await CreateClient().PostAsJsonAsync(
			"/api/auth/tokens", new { token = invitation }, JsonOptions.Web);

		withTheInvitation.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task AnInvitationReportsWhenItExpiresAndStopsWorkingThen()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");
		string link = Emails.TokenFor("nieuw@kompaz.local");

		invited.InvitationExpiresUtc.Should()
			.Be(Clock.GetUtcNow().AddDays(CustomWebApplicationFactory.InvitationLifetimeDays));

		Clock.Advance(TimeSpan.FromDays(CustomWebApplicationFactory.InvitationLifetimeDays) + TimeSpan.FromMinutes(1));

		var redeemed = await CreateClient().PostAsJsonAsync("/api/auth/tokens", new { token = link }, JsonOptions.Web);

		redeemed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task AnInvitationStillWithinItsSevenDaysWorks()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");
		string link = Emails.TokenFor("nieuw@kompaz.local");

		Clock.Advance(TimeSpan.FromDays(CustomWebApplicationFactory.InvitationLifetimeDays) - TimeSpan.FromMinutes(1));

		var redeemed = await CreateClient().PostAsJsonAsync("/api/auth/tokens", new { token = link }, JsonOptions.Web);

		redeemed.StatusCode.Should().Be(HttpStatusCode.OK);
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
