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

/// <summary>
/// Editing a user: the four fields, who may change which, and what an edit does to the access somebody already has.
/// </summary>
[TestFixture]
internal sealed class UserEditTests : ApiTestBase
{
	private const string MemberEmail = "lid@kompaz.local";
	private const string MemberName = "Gewoon Lid";

	[Test]
	public async Task APlatformAdministratorCanChangeNameEmailAndRoleAtOnce()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);

		var updated = await EditAsync(
			administrator,
			member.Id,
			new UserEndpoints.UpdateUserRequest("Nieuwe Naam", "nieuw@kompaz.local", UserRole.Administrator));

		updated.Name.Should().Be("Nieuwe Naam");
		updated.Email.Should().Be("nieuw@kompaz.local");
		updated.Role.Should().Be(UserRole.Administrator);
	}

	/// <summary>
	/// The address is the sign-in identity, so the account answers to the new one afterwards and not the old.
	/// </summary>
	[Test]
	public async Task TheNewAddressIsTheOneThatSignsIn()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);

		// The invitation already went to the old address, so "nothing was ever sent there" is not the question.
		// What matters is that nothing new arrives once the account has moved on.
		string lastSentToTheOldAddress = Emails.TokenFor(MemberEmail);

		await EditAsync(administrator, member.Id, new UserEndpoints.UpdateUserRequest(MemberName, "nieuw@kompaz.local"));

		var toTheNew = await RequestALinkAsync("nieuw@kompaz.local");
		var toTheOld = await RequestALinkAsync(MemberEmail);

		// Both answers are the same, because the endpoint never says who exists.
		toTheNew.StatusCode.Should().Be(HttpStatusCode.Accepted);
		toTheOld.StatusCode.Should().Be(HttpStatusCode.Accepted);

		Emails.WasSentTo("nieuw@kompaz.local").Should().BeTrue();
		Emails.TokenFor(MemberEmail).Should().Be(lastSentToTheOldAddress);
	}

	/// <summary>
	/// The invitation went to the address they are leaving. Whoever still reads that inbox must not be able to
	/// open an account that is no longer theirs.
	/// </summary>
	[Test]
	public async Task ALinkSentToTheOldAddressStopsWorking()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);
		string invitation = Emails.TokenFor(MemberEmail);

		await EditAsync(administrator, member.Id, new UserEndpoints.UpdateUserRequest(MemberName, "nieuw@kompaz.local"));

		var redeemed = await CreateClient().PostAsJsonAsync(
			"/api/auth/tokens", new { token = invitation }, JsonOptions.Web);

		redeemed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task AnAddressSomebodyElseHasIsRefused()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);
		await InviteAsync(administrator, "collega@kompaz.local", "Collega");

		var response = await administrator.PutAsJsonAsync(
			$"/api/users/{member.Id}",
			new UserEndpoints.UpdateUserRequest(MemberName, "collega@kompaz.local"),
			JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task AMalformedAddressIsRejected()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);

		var response = await administrator.PutAsJsonAsync(
			$"/api/users/{member.Id}",
			new UserEndpoints.UpdateUserRequest(MemberName, "geen-adres"),
			JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	/// <summary>
	/// Keeping the same address is not a change, so it is not a clash with themselves either.
	/// </summary>
	[Test]
	public async Task LeavingTheAddressAloneIsNotAConflict()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);

		var updated = await EditAsync(
			administrator, member.Id, new UserEndpoints.UpdateUserRequest("Nieuwe Naam", MemberEmail));

		updated.Email.Should().Be(MemberEmail);
		updated.Name.Should().Be("Nieuwe Naam");
	}

	// --- Who may change what

	[Test]
	public async Task AnOrganizationAdministratorCanEditNameAndEmail()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(platformAdministrator, MemberEmail, MemberName);
		var administrator = await InviteAndSignInAsync(
			platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var updated = await EditAsync(
			administrator, member.Id, new UserEndpoints.UpdateUserRequest("Nieuwe Naam", "nieuw@kompaz.local"));

		updated.Name.Should().Be("Nieuwe Naam");
		updated.Email.Should().Be("nieuw@kompaz.local");
	}

	/// <summary>
	/// Who administers an organization is the platform's call, so an organization administrator may not move
	/// anybody between roles — not even downwards, which the grant rule on its own would have allowed.
	/// </summary>
	[Test]
	public async Task AnOrganizationAdministratorMayNotChangeARole()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var colleague = await InviteAsync(
			platformAdministrator, "collega@kompaz.local", "Collega", UserRole.Administrator);
		var administrator = await InviteAndSignInAsync(
			platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var demote = await administrator.PutAsJsonAsync(
			$"/api/users/{colleague.Id}",
			new UserEndpoints.UpdateUserRequest(colleague.Name, colleague.Email, UserRole.Member),
			JsonOptions.Web);

		demote.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	/// <summary>
	/// Their form has no role field, so their request has no role in it. Omitting one has to be an edit they are
	/// allowed to make rather than an implicit demotion to whatever the default would be.
	/// </summary>
	[Test]
	public async Task OmittingTheRoleLeavesItAlone()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var colleague = await InviteAsync(
			platformAdministrator, "collega@kompaz.local", "Collega", UserRole.Administrator);
		var administrator = await InviteAndSignInAsync(
			platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var updated = await EditAsync(
			administrator, colleague.Id, new UserEndpoints.UpdateUserRequest("Nieuwe Naam", colleague.Email));

		updated.Role.Should().Be(UserRole.Administrator);
	}

	/// <summary>
	/// Sending the role they already have is not a change, so it is not a role edit and does not need the rights
	/// for one. A form that round-trips every field it loaded must not be refused for that alone.
	/// </summary>
	[Test]
	public async Task SendingTheUnchangedRoleIsNotARoleChange()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(platformAdministrator, MemberEmail, MemberName);
		var administrator = await InviteAndSignInAsync(
			platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var updated = await EditAsync(
			administrator,
			member.Id,
			new UserEndpoints.UpdateUserRequest("Nieuwe Naam", MemberEmail, UserRole.Member));

		updated.Name.Should().Be("Nieuwe Naam");
		updated.Role.Should().Be(UserRole.Member);
	}

	// --- Moving between organizations

	[Test]
	public async Task APlatformAdministratorCanMoveSomebodyAndTheRoleResets()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var tenant = await CreateOrganizationAsync(platformAdministrator);
		var colleague = await InviteAsync(
			platformAdministrator, "collega@kompaz.local", "Collega", UserRole.Administrator);

		var updated = await EditAsync(
			platformAdministrator,
			colleague.Id,
			new UserEndpoints.UpdateUserRequest(colleague.Name, colleague.Email, OrganizationId: tenant.Id));

		updated.OrganizationId.Should().Be(tenant.Id);
		updated.Role.Should().Be(UserRole.Member);
	}

	/// <summary>
	/// The move resets the role, so naming a different one in the same breath is refused rather than half-applied.
	/// </summary>
	[Test]
	public async Task MovingWhileNamingAHigherRoleIsRefused()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var tenant = await CreateOrganizationAsync(platformAdministrator);
		var member = await InviteAsync(platformAdministrator, MemberEmail, MemberName);

		var response = await platformAdministrator.PutAsJsonAsync(
			$"/api/users/{member.Id}",
			new UserEndpoints.UpdateUserRequest(MemberName, MemberEmail, UserRole.Administrator, tenant.Id),
			JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	/// <summary>
	/// Every access token they hold names the organization they have left and a role they no longer have, so the
	/// session goes with the move.
	/// </summary>
	[Test]
	public async Task MovingSomebodyEndsTheirSession()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var tenant = await CreateOrganizationAsync(platformAdministrator);
		var memberClient = await InviteAndSignInAsync(
			platformAdministrator, MemberEmail, MemberName, UserRole.Member);
		var member = await FindAsync(platformAdministrator, MemberEmail);

		(await memberClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

		await EditAsync(
			platformAdministrator,
			member.Id,
			new UserEndpoints.UpdateUserRequest(MemberName, MemberEmail, OrganizationId: tenant.Id));

		(await memberClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task AnOrganizationAdministratorMayNotMoveAnybody()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var tenant = await CreateOrganizationAsync(platformAdministrator);
		var member = await InviteAsync(platformAdministrator, MemberEmail, MemberName);
		var administrator = await InviteAndSignInAsync(
			platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var response = await administrator.PutAsJsonAsync(
			$"/api/users/{member.Id}",
			new UserEndpoints.UpdateUserRequest(MemberName, MemberEmail, OrganizationId: tenant.Id),
			JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task MovingIntoAnOrganizationThatIsNotThereIsNotFound()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(platformAdministrator, MemberEmail, MemberName);

		var response = await platformAdministrator.PutAsJsonAsync(
			$"/api/users/{member.Id}",
			new UserEndpoints.UpdateUserRequest(MemberName, MemberEmail, OrganizationId: Guid.NewGuid()),
			JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	/// <summary>
	/// Moving the last administrator out empties the same pool deleting them would, and leaves the organization
	/// just as stuck.
	/// </summary>
	[Test]
	public async Task MovingTheLastAdministratorOutOfAnOrganizationIsRefused()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var tenant = await CreateOrganizationAsync(platformAdministrator);
		var other = await CreateOrganizationAsync(platformAdministrator, "Andere B.V.");
		var onlyAdministrator = await InviteAsync(
			platformAdministrator, "beheer@klant.local", "Enige Beheerder", UserRole.Administrator, tenant.Id);

		var response = await platformAdministrator.PutAsJsonAsync(
			$"/api/users/{onlyAdministrator.Id}",
			new UserEndpoints.UpdateUserRequest(
				onlyAdministrator.Name, onlyAdministrator.Email, OrganizationId: other.Id),
			JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	/// <summary>
	/// A demotion takes the same person out of the same pool a deletion would.
	/// </summary>
	[Test]
	public async Task DemotingTheLastAdministratorOfAnOrganizationIsRefused()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var tenant = await CreateOrganizationAsync(platformAdministrator);
		var onlyAdministrator = await InviteAsync(
			platformAdministrator, "beheer@klant.local", "Enige Beheerder", UserRole.Administrator, tenant.Id);

		var response = await platformAdministrator.PutAsJsonAsync(
			$"/api/users/{onlyAdministrator.Id}",
			new UserEndpoints.UpdateUserRequest(
				onlyAdministrator.Name, onlyAdministrator.Email, UserRole.Member),
			JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	// --- What an edit does to access already granted

	/// <summary>
	/// Authorization is read from the token, which was signed before the demotion. Without a check against the row
	/// the demoted administrator would keep administering until it expired.
	/// </summary>
	[Test]
	public async Task ADemotionTakesEffectBeforeTheTokenExpires()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(platformAdministrator, "collega@kompaz.local", "Collega", UserRole.Administrator);
		var administratorClient = await InviteAndSignInAsync(
			platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);
		var administrator = await FindAsync(platformAdministrator, "beheer@kompaz.local");

		(await administratorClient.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.OK);

		await EditAsync(
			platformAdministrator,
			administrator.Id,
			new UserEndpoints.UpdateUserRequest(administrator.Name, administrator.Email, UserRole.Member));

		(await administratorClient.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	/// <summary>
	/// The stale token is refused, not the session. A client that still holds a refresh token exchanges it and
	/// comes back with claims that match the row, so a demotion costs a round trip rather than a sign-in.
	/// </summary>
	[Test]
	public async Task RefreshingAfterADemotionGivesBackTheNewRole()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(platformAdministrator, "collega@kompaz.local", "Collega", UserRole.Administrator);
		var session = await InviteAndSignInForSessionAsync(platformAdministrator, "beheer@kompaz.local", "Beheerder");
		var administrator = await FindAsync(platformAdministrator, "beheer@kompaz.local");

		await EditAsync(
			platformAdministrator,
			administrator.Id,
			new UserEndpoints.UpdateUserRequest(administrator.Name, administrator.Email, UserRole.Member));

		var refreshed = await CreateClient().PostAsJsonAsync(
			"/api/auth/tokens/refresh", new { refreshToken = session.RefreshToken }, JsonOptions.Web);
		var renewed = await refreshed.Content.ReadFromJsonAsync<Kompaz.Application.Authentication.AuthenticationResultDto>(
			JsonOptions.Web);

		refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
		renewed!.User.Role.Should().Be(UserRole.Member);
	}

	private async Task<Kompaz.Application.Authentication.AuthenticationResultDto> InviteAndSignInForSessionAsync(
		HttpClient inviter, string email, string name)
	{
		await InviteAsync(inviter, email, name, UserRole.Administrator);

		return await StartSessionAsync(email);
	}

	private static async Task<UserDto> EditAsync(HttpClient client, Guid id, UserEndpoints.UpdateUserRequest request)
	{
		var response = await client.PutAsJsonAsync($"/api/users/{id}", request, JsonOptions.Web);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions.Web)
			?? throw new InvalidOperationException("The API returned no user.");
	}

	private Task<HttpResponseMessage> RequestALinkAsync(string email) =>
		CreateClient().PostAsJsonAsync("/api/auth/magic-link", new { email }, JsonOptions.Web);

	private static async Task<OrganizationDto> CreateOrganizationAsync(HttpClient client, string name = "Klant B.V.")
	{
		var response = await client.PostAsJsonAsync(
			"/api/organizations", new CreateOrganizationCommand(name), JsonOptions.Web);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web)
			?? throw new InvalidOperationException("The API returned no organization.");
	}

	private static async Task<UserDto> FindAsync(HttpClient client, string email)
	{
		var page = await client.GetFromJsonAsync<Kompaz.Application.Common.Models.PaginatedList<UserDto>>(
			"/api/users?pageSize=100", JsonOptions.Web);

		return page!.Items.Single(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase));
	}
}
