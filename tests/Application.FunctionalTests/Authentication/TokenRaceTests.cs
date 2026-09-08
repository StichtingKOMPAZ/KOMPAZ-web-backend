using FluentAssertions;
using Kompaz.Application.Authentication;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Authentication;

/// <summary>
/// A single-use secret has to stay single-use when two requests arrive with it at the same moment, which a check
/// followed by a write cannot guarantee on its own. These drive both redemption paths concurrently.
/// </summary>
[TestFixture]
internal sealed class TokenRaceTests : ApiTestBase
{
	private const int Racers = 6;

	[Test]
	public async Task OnlyOneOfManySimultaneousRedemptionsOfALinkSucceeds()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		await InviteAsync(administrator, "haast@kompaz.local", "Veel Haast");
		string token = Emails.TokenFor("haast@kompaz.local");

		var responses = await RaceAsync("/api/auth/tokens", new { token });

		responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
		responses.Where(response => response.StatusCode != HttpStatusCode.OK)
			.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task OnlyOneOfManySimultaneousRefreshesSucceeds()
	{
		var session = await StartSessionAsync(SeededAdministratorEmail);

		var responses = await RaceAsync("/api/auth/tokens/refresh", new { refreshToken = session.RefreshToken });

		responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
		responses.Where(response => response.StatusCode != HttpStatusCode.OK)
			.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Unauthorized);
	}

	/// <summary>
	/// Losing the race to spend a refresh token is a replay like any other, so the successor the winner was handed
	/// is revoked along with the rest of the chain — the promise that a replay ends the session, not just the token.
	/// <para>
	/// Run over several sessions on purpose. What is being checked is that spending a token and inserting its
	/// successor land together, and a single round only catches the failure when the interleaving happens to fall
	/// the wrong way. Rounds make it likely rather than lucky.
	/// </para>
	/// </summary>
	[Test]
	public async Task LosingTheRaceToRefreshEndsTheSession()
	{
		for (int round = 0; round < 8; round++)
		{
			var session = await StartSessionAsync(SeededAdministratorEmail);

			var responses = await RaceAsync("/api/auth/tokens/refresh", new { refreshToken = session.RefreshToken });
			var winner = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
			var successor = await winner.Content.ReadFromJsonAsync<AuthenticationResultDto>(JsonOptions.Web);

			var afterwards = await CreateClient().PostAsJsonAsync(
				"/api/auth/tokens/refresh",
				new { refreshToken = successor!.RefreshToken },
				JsonOptions.Web);

			afterwards.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "round {0} left the successor alive", round);
		}
	}

	private async Task<IReadOnlyList<HttpResponseMessage>> RaceAsync<TBody>(string route, TBody body)
	{
		var clients = Enumerable.Range(0, Racers).Select(_ => CreateClient()).ToArray();
		var gate = new TaskCompletionSource();

		var attempts = clients.Select(async client =>
		{
			await gate.Task;
			return await client.PostAsJsonAsync(route, body, JsonOptions.Web);
		}).ToArray();

		gate.SetResult();

		return await Task.WhenAll(attempts);
	}
}
