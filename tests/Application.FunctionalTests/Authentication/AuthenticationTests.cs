using FluentAssertions;
using Kompaz.Application.Authentication;
using Kompaz.Application.Authentication.Commands.RedeemLoginToken;
using Kompaz.Application.Authentication.Commands.RequestMagicLink;
using Kompaz.Application.Users;
using Kompaz.Domain.Enums;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Authentication;

[TestFixture]
internal sealed class AuthenticationTests : ApiTestBase
{
	[Test]
	public async Task RequestingALinkForAKnownAddressSendsOne()
	{
		var client = CreateClient();

		var response = await client.PostAsJsonAsync("/api/auth/magic-link", new RequestMagicLinkCommand(SeededAdministratorEmail), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Accepted);
		Emails.WasSentTo(SeededAdministratorEmail).Should().BeTrue();
	}

	[Test]
	public async Task RequestingALinkForAnUnknownAddressLooksIdenticalButSendsNothing()
	{
		var client = CreateClient();

		var response = await client.PostAsJsonAsync("/api/auth/magic-link", new RequestMagicLinkCommand("niemand@kompaz.local"), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Accepted);
		Emails.WasSentTo("niemand@kompaz.local").Should().BeFalse();
	}

	[Test]
	public async Task RequestingALinkForAMalformedAddressIsRejected()
	{
		var client = CreateClient();

		var response = await client.PostAsJsonAsync("/api/auth/magic-link", new RequestMagicLinkCommand("not-an-email"), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task RedeemingALinkReturnsAnAccessTokenAndTheProfile()
	{
		var client = CreateClient();
		await client.PostAsJsonAsync("/api/auth/magic-link", new RequestMagicLinkCommand(SeededAdministratorEmail), JsonOptions.Web);

		var response = await client.PostAsJsonAsync("/api/auth/tokens", new RedeemLoginTokenCommand(Emails.TokenFor(SeededAdministratorEmail)), JsonOptions.Web);
		var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>(JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		result.Should().NotBeNull();
		result!.TokenType.Should().Be("Bearer");
		result.AccessToken.Should().NotBeNullOrWhiteSpace();
		result.ExpiresUtc.Should().BeAfter(Clock.GetUtcNow());
		result.User.Email.Should().Be(SeededAdministratorEmail);
		result.User.Role.Should().Be(UserRole.PlatformAdministrator);
		result.User.Status.Should().Be(UserStatus.Active);
	}

	[Test]
	public async Task ALinkOnlyWorksOnce()
	{
		var client = CreateClient();
		await client.PostAsJsonAsync("/api/auth/magic-link", new RequestMagicLinkCommand(SeededAdministratorEmail), JsonOptions.Web);
		string token = Emails.TokenFor(SeededAdministratorEmail);

		var first = await client.PostAsJsonAsync("/api/auth/tokens", new RedeemLoginTokenCommand(token), JsonOptions.Web);
		var second = await client.PostAsJsonAsync("/api/auth/tokens", new RedeemLoginTokenCommand(token), JsonOptions.Web);

		first.StatusCode.Should().Be(HttpStatusCode.OK);
		second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task RequestingASecondLinkRetiresTheFirst()
	{
		var client = CreateClient();
		await client.PostAsJsonAsync("/api/auth/magic-link", new RequestMagicLinkCommand(SeededAdministratorEmail), JsonOptions.Web);
		string first = Emails.TokenFor(SeededAdministratorEmail);

		await client.PostAsJsonAsync("/api/auth/magic-link", new RequestMagicLinkCommand(SeededAdministratorEmail), JsonOptions.Web);
		string second = Emails.TokenFor(SeededAdministratorEmail);

		second.Should().NotBe(first);
		(await client.PostAsJsonAsync("/api/auth/tokens", new RedeemLoginTokenCommand(first), JsonOptions.Web))
			.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		(await client.PostAsJsonAsync("/api/auth/tokens", new RedeemLoginTokenCommand(second), JsonOptions.Web))
			.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Test]
	public async Task RedeemingAnUnknownTokenIsRejected()
	{
		var client = CreateClient();

		var response = await client.PostAsJsonAsync("/api/auth/tokens", new RedeemLoginTokenCommand("not-a-real-token"), JsonOptions.Web);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task TheCurrentUserEndpointRequiresABearerToken()
	{
		var client = CreateClient();

		var response = await client.GetAsync("/api/auth/me");

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task TheCurrentUserEndpointReturnsTheSignedInProfile()
	{
		var client = await SignInAsPlatformAdministratorAsync();

		var user = await client.GetFromJsonAsync<UserDto>("/api/auth/me", JsonOptions.Web);

		user.Should().NotBeNull();
		user!.Email.Should().Be(SeededAdministratorEmail);
		user.LastLoginUtc.Should().NotBeNull();
	}
}
