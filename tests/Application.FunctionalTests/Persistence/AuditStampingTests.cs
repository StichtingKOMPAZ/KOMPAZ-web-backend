using FluentAssertions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Users;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using Kompaz.Presentation.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Persistence;

/// <summary>
/// Audit fields are stamped while saving rather than by each handler, so no write can forget them and two writes in
/// one request cannot disagree about what time it is. These check that they are actually filled in, and that the
/// writes with no caller behind them — seeding, and activating a user who is in the middle of signing in — still
/// get their timestamps.
/// </summary>
[TestFixture]
internal sealed class AuditStampingTests : ApiTestBase
{
	[Test]
	public async Task CreatingSomebodyRecordsWhenAndByWhom()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var me = await administrator.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);
		var invited = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");

		var stored = await FindUserAsync(invited.Id);

		stored.CreatedUtc.Should().Be(Clock.GetUtcNow());
		stored.UpdatedUtc.Should().Be(Clock.GetUtcNow());
		stored.CreatedBy.Should().Be(me!.Id);
		stored.UpdatedBy.Should().Be(me.Id);
	}

	[Test]
	public async Task ChangingSomebodyRecordsWhoChangedThemWithoutTouchingWhoMadeThem()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(platformAdministrator, "nieuw@kompaz.local", "Nieuwe Collega");
		var creator = await platformAdministrator.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);

		var administrator = await InviteAndSignInAsync(
			platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);
		var editor = await administrator.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);

		Clock.Advance(TimeSpan.FromMinutes(5));

		var response = await administrator.PutAsJsonAsync(
			$"/api/users/{invited.Id}",
			new UserEndpoints.UpdateUserRequest("Hernoemd", UserRole.Member), JsonOptions.Web);
		response.EnsureSuccessStatusCode();

		var stored = await FindUserAsync(invited.Id);

		stored.CreatedBy.Should().Be(creator!.Id);
		stored.UpdatedBy.Should().Be(editor!.Id);
		stored.UpdatedUtc.Should().Be(Clock.GetUtcNow());
		stored.CreatedUtc.Should().Be(Clock.GetUtcNow() - TimeSpan.FromMinutes(5));
	}

	/// <summary>
	/// The seeder runs before anybody can sign in, so there is no author to record — but the timestamps still have to
	/// be real. Skipping the stamp altogether when there is no caller would leave the first organization dated year
	/// one, which is why only the two identifier columns are treated as optional.
	/// </summary>
	[Test]
	public async Task SeededRowsAreTimestampedWithoutAnAuthor()
	{
		using var scope = CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

		var organization = await context.Organizations.AsNoTracking().FirstAsync();

		organization.CreatedUtc.Should().Be(Clock.GetUtcNow());
		organization.UpdatedUtc.Should().Be(Clock.GetUtcNow());
		organization.CreatedBy.Should().BeNull();
		organization.UpdatedBy.Should().BeNull();
	}

	/// <summary>
	/// Redeeming a sign-in link activates the user, and the caller has no access token yet at that moment.
	/// </summary>
	[Test]
	public async Task AnAnonymousChangeIsTimestampedWithoutAnAuthor()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");

		Clock.Advance(TimeSpan.FromMinutes(3));
		await SignInAsync("nieuw@kompaz.local");

		var stored = await FindUserAsync(invited.Id);

		stored.Status.Should().Be(UserStatus.Active);
		stored.UpdatedUtc.Should().Be(Clock.GetUtcNow());
		stored.UpdatedBy.Should().BeNull();
	}

	private async Task<User> FindUserAsync(Guid id)
	{
		using var scope = CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

		return await context.Users.AsNoTracking().SingleAsync(user => user.Id == id);
	}
}
