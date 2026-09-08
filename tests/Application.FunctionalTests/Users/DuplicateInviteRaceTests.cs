using FluentAssertions;
using Kompaz.Application.Common.Models;
using Kompaz.Application.Users;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Users;

/// <summary>
/// Whatever order simultaneous requests for the same new thing land in, one of it exists afterwards and nobody gets
/// a server error. Which mechanism settles it varies: usually the second request finds the first one's row and
/// re-invites, and only a closer interleaving reaches the unique index — that path is pinned deterministically in
/// <see cref="Persistence.ConflictTranslationTests"/> instead, because a race cannot be relied on to take it.
/// </summary>
[TestFixture]
internal sealed class DuplicateInviteRaceTests : ApiTestBase
{
	private const int Racers = 6;

	[Test]
	public async Task SimultaneousInvitationsOfOneNewAddressLeaveOneUser()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();

		var responses = await RaceAsync(administrator, "gelijk@kompaz.local");

		// Whoever loses either sees the pre-check and re-invites, or trips the index; neither is a 500.
		responses.Should().OnlyContain(response =>
			response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.Conflict);

		var page = await administrator.GetFromJsonAsync<PaginatedList<UserDto>>(
			"/api/users?search=gelijk", JsonOptions.Web);

		page!.Items.Should().ContainSingle();
	}

	[Test]
	public async Task SimultaneousInvitationsOfAnAddressAlreadyTakenNeverReturnAServerError()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();

		var responses = await RaceAsync(administrator, SeededAdministratorEmail);

		responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Conflict);
	}

	/// <summary>
	/// Two organizations cannot share a name either, and the same two statements guard it.
	/// </summary>
	[Test]
	public async Task SimultaneousOrganizationsOfOneNameLeaveOne()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var gate = new TaskCompletionSource();

		var attempts = Enumerable.Range(0, Racers).Select(async _ =>
		{
			await gate.Task;
			return await administrator.PostAsJsonAsync("/api/organizations", new { name = "Gelijknamig" }, JsonOptions.Web);
		}).ToArray();

		gate.SetResult();
		var responses = await Task.WhenAll(attempts);

		responses.Count(response => response.StatusCode == HttpStatusCode.Created).Should().Be(1);
		responses.Where(response => response.StatusCode != HttpStatusCode.Created)
			.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Conflict);
	}

	private static async Task<IReadOnlyList<HttpResponseMessage>> RaceAsync(HttpClient administrator, string email)
	{
		var gate = new TaskCompletionSource();

		var attempts = Enumerable.Range(0, Racers).Select(async _ =>
		{
			await gate.Task;
			return await administrator.PostAsJsonAsync(
				"/api/users/invitations", new { email, name = "Gelijk Aangekomen" }, JsonOptions.Web);
		}).ToArray();

		gate.SetResult();

		return await Task.WhenAll(attempts);
	}
}
