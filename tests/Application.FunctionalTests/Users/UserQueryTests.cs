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

	/// <summary>
	/// The invited tab needs one row per invitee carrying the four columns beside the status badge, and the badge
	/// itself comes off the link's expiry: an invitation nobody accepted stays on the list once it lapses, which is
	/// the difference between "uitgenodigd" and "verlopen".
	/// </summary>
	[Test]
	public async Task TheInvitedListCarriesEveryColumnTheTableShows()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var created = await platformAdministrator.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var tenant = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);
		await InviteAsync(platformAdministrator, "klant@kompaz.local", "Klant Beheerder", UserRole.Administrator, tenant!.Id);

		var invited = await platformAdministrator.GetFromJsonAsync<PaginatedList<UserDto>>($"/api/users?status={UserStatus.Invited}", JsonOptions.Web);
		var row = invited!.Items.Should().ContainSingle(user => user.Email == "klant@kompaz.local").Subject;

		row.Name.Should().Be("Klant Beheerder");
		row.OrganizationName.Should().Be("Klant B.V.");
		row.Role.Should().Be(UserRole.Administrator);
		row.Status.Should().Be(UserStatus.Invited);
		row.InvitationExpiresUtc.Should().BeAfter(Clock.GetUtcNow());
	}

	[Test]
	public async Task AnInvitationNobodyAcceptedStaysOnTheListOnceItLapses()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "verlopen@kompaz.local", "Verlopen Uitnodiging");

		Clock.Advance(TimeSpan.FromDays(CustomWebApplicationFactory.InvitationLifetimeDays + 1));

		// The session above lapsed along with the invitation, so the list is read on a fresh one.
		var afterwards = await SignInAsPlatformAdministratorAsync();
		var invited = await afterwards.GetFromJsonAsync<PaginatedList<UserDto>>($"/api/users?status={UserStatus.Invited}", JsonOptions.Web);
		var row = invited!.Items.Should().ContainSingle(user => user.Email == "verlopen@kompaz.local").Subject;

		row.Status.Should().Be(UserStatus.Invited);
		row.InvitationExpiresUtc.Should().BeBefore(Clock.GetUtcNow());
	}

	/// <summary>
	/// Accepting spends the link, so an active user has no outstanding invitation to report an expiry for. A badge
	/// worked out from the invitation date and the configured lifetime would have claimed one either way.
	/// </summary>
	[Test]
	public async Task AnAcceptedInvitationLeavesNothingOutstanding()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAndSignInAsync(administrator, "actief@kompaz.local", "Actief Lid", UserRole.Member);

		var active = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>($"/api/users?status={UserStatus.Active}", JsonOptions.Web);
		var row = active!.Items.Should().ContainSingle(user => user.Email == "actief@kompaz.local").Subject;

		row.InvitationExpiresUtc.Should().BeNull();
	}

	/// <summary>
	/// The tenant administrator's own invited list is the one their beheer page shows, and nobody else's invitees
	/// belong on it.
	/// </summary>
	[Test]
	public async Task AdministratorsOnlySeeInvitationsIntoTheirOwnOrganization()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var created = await platformAdministrator.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var tenant = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);

		await InviteAsync(platformAdministrator, "elders@kompaz.local", "Elders Uitgenodigd");
		await InviteAndSignInAsync(platformAdministrator, "klant@kompaz.local", "Klant Beheerder", UserRole.Administrator, tenant!.Id);

		var administrator = await SignInAsync("klant@kompaz.local");
		await InviteAsync(administrator, "instructeur@klant.local", "Instructeur");

		var invited = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>($"/api/users?status={UserStatus.Invited}", JsonOptions.Web);

		invited!.Items.Should().OnlyContain(user => user.OrganizationId == tenant.Id);
		invited.Items.Should().ContainSingle(user => user.Email == "instructeur@klant.local");
	}

	/// <summary>
	/// Revoking an invitation is a plain delete, and it takes the outstanding link with it rather than leaving one
	/// that would sign the invitee in after they were removed.
	/// </summary>
	[Test]
	public async Task RevokingAnInvitationRemovesTheRowAndKillsTheLink()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "ingetrokken@kompaz.local", "Ingetrokken Uitnodiging");
		string link = Emails.TokenFor("ingetrokken@kompaz.local");

		var deleted = await administrator.DeleteAsync($"/api/users/{invited.Id}");
		var remaining = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>($"/api/users?status={UserStatus.Invited}", JsonOptions.Web);
		var redeemed = await CreateClient().PostAsJsonAsync("/api/auth/tokens", new { token = link }, JsonOptions.Web);

		deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
		remaining!.Items.Should().NotContain(user => user.Email == "ingetrokken@kompaz.local");
		redeemed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task AdministratorsMayRevokeAnInvitationInTheirOwnOrganizationOnly()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var created = await platformAdministrator.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand("Klant B.V."), JsonOptions.Web);
		var tenant = await created.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);
		var elsewhere = await InviteAsync(platformAdministrator, "elders@kompaz.local", "Elders Uitgenodigd");
		await InviteAndSignInAsync(platformAdministrator, "klant@kompaz.local", "Klant Beheerder", UserRole.Administrator, tenant!.Id);

		var administrator = await SignInAsync("klant@kompaz.local");
		var own = await InviteAsync(administrator, "instructeur@klant.local", "Instructeur");

		var mine = await administrator.DeleteAsync($"/api/users/{own.Id}");
		var theirs = await administrator.DeleteAsync($"/api/users/{elsewhere.Id}");

		mine.StatusCode.Should().Be(HttpStatusCode.NoContent);
		theirs.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
