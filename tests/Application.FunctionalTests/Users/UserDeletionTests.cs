using FluentAssertions;
using Kompaz.Application.Common.Models;
using Kompaz.Application.Organizations;
using Kompaz.Application.Organizations.Commands.CreateOrganization;
using Kompaz.Application.Users;
using Kompaz.Domain.Enums;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Users;

/// <summary>
/// Deleting a user: the access that has to stop, the row that has to stay, the notice that goes out, and the undo.
/// </summary>
[TestFixture]
internal sealed class UserDeletionTests : ApiTestBase
{
	private const string MemberEmail = "lid@kompaz.local";
	private const string MemberName = "Gewoon Lid";

	/// <summary>
	/// The roster column the ticket describes is the active one, and an active user is the one with sessions and
	/// links to clear up — which the invited user the older test deletes does not have.
	/// </summary>
	[Test]
	public async Task AnActiveUserCanBeDeleted()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAndSignInAsync(administrator, MemberEmail, MemberName, UserRole.Member);
		var member = await FindAsync(administrator, MemberEmail);

		member.Status.Should().Be(UserStatus.Active);

		var deleted = await administrator.DeleteAsync($"/api/users/{member.Id}");

		deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	/// <summary>
	/// The point of the whole feature. An access token is a signed statement about the past, so without a check on
	/// every request a deleted user would keep the rest of their hour.
	/// </summary>
	[Test]
	public async Task ADeletedUserLosesAccessImmediately()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var memberClient = await InviteAndSignInAsync(administrator, MemberEmail, MemberName, UserRole.Member);
		var member = await FindAsync(administrator, MemberEmail);

		(await memberClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

		await administrator.DeleteAsync($"/api/users/{member.Id}");

		(await memberClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task ADeletedUserIsToldByEmail()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);

		await administrator.DeleteAsync($"/api/users/{member.Id}");

		Emails.ToldAboutDeletion(MemberEmail).Should().BeTrue();
	}

	/// <summary>
	/// A link already sitting in an inbox has to stop working the moment the account does, which is why the tokens
	/// are deleted outright rather than marked along with the user.
	/// </summary>
	[Test]
	public async Task ADeletedUsersOutstandingLinkStopsWorking()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);
		string invitation = Emails.TokenFor(MemberEmail);

		await administrator.DeleteAsync($"/api/users/{member.Id}");

		var redeemed = await CreateClient().PostAsJsonAsync(
			"/api/auth/tokens", new { token = invitation }, JsonOptions.Web);

		redeemed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	/// <summary>
	/// Asking for a link as a deleted user gets the same answer as asking for one that was never an account, for
	/// the same reason: the endpoint does not say who exists.
	/// </summary>
	[Test]
	public async Task ADeletedUserCannotRequestASignInLink()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);
		await administrator.DeleteAsync($"/api/users/{member.Id}");

		var response = await CreateClient().PostAsJsonAsync(
			"/api/auth/magic-link", new { email = MemberEmail }, JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Accepted);
		Emails.WasSentTo(MemberEmail).Should().BeFalse();
	}

	[Test]
	public async Task ADeletedUserLeavesTheRosterButCanStillBeListedOnPurpose()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);

		await administrator.DeleteAsync($"/api/users/{member.Id}");

		var roster = await ListAsync(administrator, includeDeleted: false);
		var withDeleted = await ListAsync(administrator, includeDeleted: true);

		roster.Should().NotContain(user => user.Id == member.Id);
		withDeleted.Should().ContainSingle(user => user.Id == member.Id)
			.Which.DeletedUtc.Should().NotBeNull();
	}

	[Test]
	public async Task DeletingSomebodyWhoIsAlreadyDeletedIsNotFound()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);

		await administrator.DeleteAsync($"/api/users/{member.Id}");
		var again = await administrator.DeleteAsync($"/api/users/{member.Id}");

		again.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task ADeletedUserCannotBeEdited()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);
		await administrator.DeleteAsync($"/api/users/{member.Id}");

		var response = await administrator.PutAsJsonAsync(
			$"/api/users/{member.Id}",
			new { name = "Andere Naam", role = UserRole.Member },
			JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task RestoringPutsTheUserBackExactlyAsTheyWere()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAndSignInAsync(administrator, MemberEmail, MemberName, UserRole.Member);
		var member = await FindAsync(administrator, MemberEmail);

		await administrator.DeleteAsync($"/api/users/{member.Id}");
		var restored = await RestoreAsync(administrator, member.Id);

		restored.StatusCode.Should().Be(HttpStatusCode.OK);

		var back = await restored.Content.ReadFromJsonAsync<UserDto>(JsonOptions.Web);
		back!.DeletedUtc.Should().BeNull();
		back.Status.Should().Be(UserStatus.Active);
		back.Name.Should().Be(MemberName);
		back.Role.Should().Be(UserRole.Member);

		(await ListAsync(administrator, includeDeleted: false))
			.Should().ContainSingle(user => user.Id == member.Id);
	}

	/// <summary>
	/// Restoring does not hand the credentials back: the links and sessions were deleted, so a restored user comes
	/// in through the login page like anybody else.
	/// </summary>
	[Test]
	public async Task ARestoredUserSignsInAgain()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAndSignInAsync(administrator, MemberEmail, MemberName, UserRole.Member);
		var member = await FindAsync(administrator, MemberEmail);

		await administrator.DeleteAsync($"/api/users/{member.Id}");
		await RestoreAsync(administrator, member.Id);

		var freshClient = await SignInAsync(MemberEmail);

		(await freshClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// Two administrators reaching for the same undo, or one clicking twice, should both be told the account is
	/// back rather than one of them being told they were too late.
	/// </summary>
	[Test]
	public async Task RestoringSomebodyWhoIsNotDeletedIsAccepted()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);

		var response = await RestoreAsync(administrator, member.Id);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// The row keeps the address, which is the unique key, so inviting it again has to land on the same row rather
	/// than being refused as a clash — otherwise deleting somebody burns their email address for good.
	/// </summary>
	[Test]
	public async Task InvitingADeletedAddressBringsBackTheSamePerson()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAndSignInAsync(administrator, MemberEmail, MemberName, UserRole.Member);
		var member = await FindAsync(administrator, MemberEmail);

		await administrator.DeleteAsync($"/api/users/{member.Id}");

		var reinvited = await InviteAsync(administrator, MemberEmail, "Teruggekomen Lid", UserRole.Administrator);

		reinvited.Id.Should().Be(member.Id);
		reinvited.DeletedUtc.Should().BeNull();
		reinvited.Status.Should().Be(UserStatus.Invited);
		reinvited.Name.Should().Be("Teruggekomen Lid");
		reinvited.Role.Should().Be(UserRole.Administrator);
	}

	/// <summary>
	/// An organization with no administrator left is stuck: only an administrator can invite, and only an
	/// administrator can appoint one, so recovering needs a platform administrator and a support call.
	/// </summary>
	[Test]
	public async Task TheLastAdministratorOfAnOrganizationCannotBeDeleted()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateOrganizationAsync(platformAdministrator);
		var onlyAdministrator = await InviteAsync(
			platformAdministrator, "beheer@klant.local", "Enige Beheerder", UserRole.Administrator, organization.Id);

		var response = await platformAdministrator.DeleteAsync($"/api/users/{onlyAdministrator.Id}");

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task AnAdministratorCanBeDeletedWhileAnotherRemains()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateOrganizationAsync(platformAdministrator);
		var first = await InviteAsync(
			platformAdministrator, "beheer1@klant.local", "Eerste Beheerder", UserRole.Administrator, organization.Id);
		await InviteAsync(
			platformAdministrator, "beheer2@klant.local", "Tweede Beheerder", UserRole.Administrator, organization.Id);

		var response = await platformAdministrator.DeleteAsync($"/api/users/{first.Id}");

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	/// <summary>
	/// A deleted administrator is no longer somebody who could appoint a replacement, so they must not be the
	/// reason a further deletion is allowed.
	/// </summary>
	[Test]
	public async Task ADeletedAdministratorDoesNotCountAsTheOneWhoRemains()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateOrganizationAsync(platformAdministrator);
		var first = await InviteAsync(
			platformAdministrator, "beheer1@klant.local", "Eerste Beheerder", UserRole.Administrator, organization.Id);
		var second = await InviteAsync(
			platformAdministrator, "beheer2@klant.local", "Tweede Beheerder", UserRole.Administrator, organization.Id);

		(await platformAdministrator.DeleteAsync($"/api/users/{first.Id}"))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var response = await platformAdministrator.DeleteAsync($"/api/users/{second.Id}");

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	/// <summary>
	/// "Deleted means gone" holds for every endpoint that addresses one person. The roster asked with
	/// <c>includeDeleted</c> is the single read that sees them, which is where a restore starts.
	/// </summary>
	[Test]
	public async Task ADeletedUserIsNotFoundByIdEither()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAsync(administrator, MemberEmail, MemberName);
		await administrator.DeleteAsync($"/api/users/{member.Id}");

		var response = await administrator.GetAsync($"/api/users/{member.Id}");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	private static Task<HttpResponseMessage> RestoreAsync(HttpClient client, Guid id) =>
		client.PostAsync($"/api/users/{id}/restore", content: null);

	private static async Task<OrganizationDto> CreateOrganizationAsync(HttpClient client)
	{
		var response = await client.PostAsJsonAsync(
			"/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web)
			?? throw new InvalidOperationException("The API returned no organization.");
	}

	private static async Task<IReadOnlyCollection<UserDto>> ListAsync(HttpClient client, bool includeDeleted)
	{
		var page = await client.GetFromJsonAsync<PaginatedList<UserDto>>(
			$"/api/users?includeDeleted={includeDeleted}&pageSize=100", JsonOptions.Web);

		return page!.Items;
	}

	private static async Task<UserDto> FindAsync(HttpClient client, string email)
	{
		var users = await ListAsync(client, includeDeleted: true);

		return users.Single(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase));
	}
}
