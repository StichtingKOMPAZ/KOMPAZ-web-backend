using FluentAssertions;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Per-client rate limiting keys on the caller's address, so behind a proxy it is only per-client once the forwarded
/// headers are believed — and believing them from the wrong sender turns the limit into something the caller picks.
/// These pin both halves by exhausting a tiny magic-link budget from two forwarded addresses.
/// </summary>
[TestFixture]
internal sealed class ForwardedHeadersTests
{
	private const string FirstCaller = "203.0.113.1";
	private const string SecondCaller = "203.0.113.2";

	/// <summary>
	/// The safe default. Nothing has been named, so the header is ignored and both callers share the one partition
	/// the connection's address gives them.
	/// </summary>
	[Test]
	public async Task WithoutAConfiguredProxyForwardedCallersShareOneBudget()
	{
		using var factory = TightMagicLinkBudget([]);

		await ExhaustMagicLinkBudgetAsync(factory, FirstCaller);

		(await RequestMagicLinkAsync(factory, SecondCaller)).Should().Be(HttpStatusCode.TooManyRequests);
	}

	[Test]
	public async Task WithAProxyConfiguredEachForwardedCallerGetsItsOwnBudget()
	{
		using var factory = TightMagicLinkBudget(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["ForwardedHeaders:TrustAnyProxy"] = "true",
		});

		await ExhaustMagicLinkBudgetAsync(factory, FirstCaller);

		(await RequestMagicLinkAsync(factory, SecondCaller)).Should().Be(HttpStatusCode.Accepted);
	}

	[Test]
	public async Task AConfiguredProxyStillLimitsTheSameForwardedCaller()
	{
		using var factory = TightMagicLinkBudget(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["ForwardedHeaders:TrustAnyProxy"] = "true",
		});

		await ExhaustMagicLinkBudgetAsync(factory, FirstCaller);

		(await RequestMagicLinkAsync(factory, FirstCaller)).Should().Be(HttpStatusCode.TooManyRequests);
	}

	/// <summary>
	/// Naming proxies and trusting any are two different intentions, and holding both is a mistake worth hearing
	/// about at startup rather than discovering from a rate limit that never triggers.
	/// </summary>
	[Test]
	public void ContradictoryProxyConfigurationIsRefusedAtStartup()
	{
		using var factory = new CustomWebApplicationFactory(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["ForwardedHeaders:TrustAnyProxy"] = "true",
			["ForwardedHeaders:KnownProxies:0"] = "10.0.0.1",
		});

		Action start = () => factory.CreateClient();

		start.Should().Throw<Exception>().Which.Message.Should().Contain("TrustAnyProxy");
	}

	[Test]
	public void AnAddressThatIsNotAnAddressIsRefusedAtStartup()
	{
		using var factory = new CustomWebApplicationFactory(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["ForwardedHeaders:KnownNetworks:0"] = "not-a-network",
		});

		Action start = () => factory.CreateClient();

		start.Should().Throw<Exception>().Which.Message.Should().Contain("not-a-network");
	}

	private static CustomWebApplicationFactory TightMagicLinkBudget(Dictionary<string, string> settings)
	{
		settings["RateLimiting:MagicLinkPermitLimit"] = "2";
		settings["RateLimiting:MagicLinkWindowSeconds"] = "300";

		return new CustomWebApplicationFactory(settings);
	}

	private static async Task ExhaustMagicLinkBudgetAsync(CustomWebApplicationFactory factory, string caller)
	{
		for (int attempt = 0; attempt < 2; attempt++)
		{
			(await RequestMagicLinkAsync(factory, caller)).Should().Be(HttpStatusCode.Accepted);
		}

		(await RequestMagicLinkAsync(factory, caller)).Should().Be(HttpStatusCode.TooManyRequests);
	}

	private static async Task<HttpStatusCode> RequestMagicLinkAsync(CustomWebApplicationFactory factory, string caller)
	{
		using var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Forwarded-For", caller);

		var response = await client.PostAsJsonAsync(
			"/api/auth/magic-link", new { email = "niemand@kompaz.local" }, JsonOptions.Web);

		return response.StatusCode;
	}
}
