using FluentAssertions;
using Kompaz.Application.Authentication;
using Kompaz.Application.Authentication.Commands.RefreshAccessToken;
using Kompaz.Application.Authentication.Commands.RevokeRefreshToken;
using Kompaz.Application.Users;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Authentication;

[TestFixture]
internal sealed class RefreshTokenTests : ApiTestBase
{
	[Test]
	public async Task SigningInHandsOutARefreshTokenAlongsideTheAccessToken()
	{
		var session = await StartSessionAsync(SeededAdministratorEmail);

		session.RefreshToken.Should().NotBeNullOrWhiteSpace();
		session.RefreshToken.Should().NotBe(session.AccessToken);
		session.RefreshTokenExpiresUtc.Should().BeAfter(session.ExpiresUtc);
	}

	[Test]
	public async Task RefreshingReturnsAWorkingAccessTokenWithoutAnotherEmail()
	{
		var session = await StartSessionAsync(SeededAdministratorEmail);
		var anonymous = CreateClient();

		var response = await anonymous.PostAsJsonAsync("/api/auth/tokens/refresh", new RefreshAccessTokenCommand(session.RefreshToken), JsonOptions.Web);
		var refreshed = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>(JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		refreshed!.User.Email.Should().Be(SeededAdministratorEmail);

		var profile = await Authenticated(refreshed).GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);
		profile!.Email.Should().Be(SeededAdministratorEmail);
	}

	[Test]
	public async Task RefreshingRotatesTheRefreshToken()
	{
		var session = await StartSessionAsync(SeededAdministratorEmail);

		var refreshed = await RefreshAsync(session.RefreshToken);

		refreshed.RefreshToken.Should().NotBe(session.RefreshToken);
	}

	[Test]
	public async Task TheExpirySlidesForwardOnEveryRefresh()
	{
		var session = await StartSessionAsync(SeededAdministratorEmail);

		Clock.Advance(TimeSpan.FromDays(10));
		var first = await RefreshAsync(session.RefreshToken);

		Clock.Advance(TimeSpan.FromDays(10));
		var second = await RefreshAsync(first.RefreshToken);

		first.RefreshTokenExpiresUtc.Should().Be(session.RefreshTokenExpiresUtc.AddDays(10));
		second.RefreshTokenExpiresUtc.Should().Be(first.RefreshTokenExpiresUtc.AddDays(10));
	}

	[Test]
	public async Task AnActiveSessionOutlivesTheOriginalSlidingWindow()
	{
		var session = await StartSessionAsync(SeededAdministratorEmail);

		// Four hops of ten days each: forty days in, well past the original fourteen-day window, but never idle
		// long enough to lapse.
		var lastHop = await KeepSessionAliveAsync(session, hops: 4, hopLength: TimeSpan.FromDays(10));

		Clock.GetUtcNow().Should().BeAfter(session.RefreshTokenExpiresUtc);
		lastHop.RefreshTokenExpiresUtc.Should().BeAfter(Clock.GetUtcNow());
	}

	[Test]
	public async Task TheSlideNeverPassesTheAbsoluteDeadline()
	{
		var absoluteDeadline = Clock.GetUtcNow().AddDays(CustomWebApplicationFactory.AbsoluteLifetimeDays);

		// Eight ten-day hops reach day eighty, where a fresh fourteen-day window would run to day ninety-four.
		var lastHop = await KeepSessionAliveAsync(hops: 8, hopLength: TimeSpan.FromDays(10));

		lastHop.RefreshTokenExpiresUtc.Should().Be(absoluteDeadline);
	}

	[Test]
	public async Task ASessionEndsOnceItsAbsoluteDeadlinePasses()
	{
		var lastHop = await KeepSessionAliveAsync(hops: 8, hopLength: TimeSpan.FromDays(10));

		// Day ninety-one: inside the sliding window the last hop granted, but past the ceiling it was clamped to.
		Clock.Advance(TimeSpan.FromDays(11));
		var response = await AttemptRefreshAsync(lastHop.RefreshToken);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task AnIdleSessionLapsesAfterTheSlidingWindow()
	{
		var session = await StartSessionAsync(SeededAdministratorEmail);

		Clock.Advance(TimeSpan.FromDays(15));
		var response = await AttemptRefreshAsync(session.RefreshToken);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task ReplayingASpentRefreshTokenEndsTheWholeSession()
	{
		var session = await StartSessionAsync(SeededAdministratorEmail);
		var refreshed = await RefreshAsync(session.RefreshToken);

		var replay = await AttemptRefreshAsync(session.RefreshToken);
		var successorAfterReplay = await AttemptRefreshAsync(refreshed.RefreshToken);

		replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		successorAfterReplay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task ReplayLeavesOtherSessionsAlone()
	{
		var first = await StartSessionAsync(SeededAdministratorEmail);
		var second = await StartSessionAsync(SeededAdministratorEmail);

		await RefreshAsync(first.RefreshToken);
		await AttemptRefreshAsync(first.RefreshToken);

		var stillWorks = await AttemptRefreshAsync(second.RefreshToken);

		stillWorks.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Test]
	public async Task RevokingEndsTheSession()
	{
		var session = await StartSessionAsync(SeededAdministratorEmail);
		var anonymous = CreateClient();

		var revoked = await anonymous.PostAsJsonAsync("/api/auth/tokens/revoke", new RevokeRefreshTokenCommand(session.RefreshToken), JsonOptions.Web);
		var afterRevoke = await AttemptRefreshAsync(session.RefreshToken);

		revoked.StatusCode.Should().Be(HttpStatusCode.NoContent);
		afterRevoke.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task RevokingAnUnknownTokenStillSucceedsSoAClientCanAlwaysSignOut()
	{
		var anonymous = CreateClient();

		var response = await anonymous.PostAsJsonAsync("/api/auth/tokens/revoke", new RevokeRefreshTokenCommand("not-a-real-token"), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task RefreshingWithAnUnknownTokenIsRejected()
	{
		var response = await AttemptRefreshAsync("not-a-real-token");

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task DeletingAUserEndsTheirSessions()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		var invited = await InviteAsync(administrator, "nieuw@kompaz.local", "Nieuwe Collega");
		var session = await StartSessionAsync("nieuw@kompaz.local");

		await administrator.DeleteAsync($"/api/users/{invited.Id}");
		var response = await AttemptRefreshAsync(session.RefreshToken);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	/// <summary>
	/// Opens a session and refreshes it every <paramref name="hopLength"/>, returning the last hop.
	/// </summary>
	private async Task<AuthenticationResultDto> KeepSessionAliveAsync(int hops, TimeSpan hopLength) =>
		await KeepSessionAliveAsync(await StartSessionAsync(SeededAdministratorEmail), hops, hopLength);

	private async Task<AuthenticationResultDto> KeepSessionAliveAsync(AuthenticationResultDto session, int hops, TimeSpan hopLength)
	{
		var latest = session;

		for (int hop = 0; hop < hops; hop++)
		{
			Clock.Advance(hopLength);
			latest = await RefreshAsync(latest.RefreshToken);
		}

		return latest;
	}

	private async Task<AuthenticationResultDto> RefreshAsync(string refreshToken)
	{
		var response = await AttemptRefreshAsync(refreshToken);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadFromJsonAsync<AuthenticationResultDto>(JsonOptions.Web)
			?? throw new InvalidOperationException("The API returned no authentication result.");
	}

	private Task<HttpResponseMessage> AttemptRefreshAsync(string refreshToken) =>
		CreateClient().PostAsJsonAsync("/api/auth/tokens/refresh", new RefreshAccessTokenCommand(refreshToken), JsonOptions.Web);

	private HttpClient Authenticated(AuthenticationResultDto session)
	{
		var client = CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(session.TokenType, session.AccessToken);

		return client;
	}
}
