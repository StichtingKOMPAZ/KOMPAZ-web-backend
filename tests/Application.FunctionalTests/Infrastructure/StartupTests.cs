using FluentAssertions;
using NUnit.Framework;
using System.Net;

namespace Kompaz.Application.FunctionalTests.Infrastructure;

/// <summary>
/// What the application does to itself before it serves anything, and what it refuses to serve at all.
/// </summary>
[TestFixture]
internal sealed class StartupTests
{
	/// <summary>
	/// A deployment that applies migrations itself would otherwise run against whatever schema happened to be there.
	/// </summary>
	[Test]
	public void ADatabaseBehindTheCodeIsRefusedWhenTheApplicationDoesNotMigrateIt()
	{
		using var factory = new CustomWebApplicationFactory(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["Database:MigrateOnStartup"] = "false",
		});

		Action start = () => factory.CreateClient();

		start.Should().Throw<Exception>()
			.Which.Message.Should().Contain("Database:MigrateOnStartup");
	}

	[Test]
	public void MigratingOnStartupIsTheDefault()
	{
		using var factory = new CustomWebApplicationFactory();

		Action start = () => factory.CreateClient();

		start.Should().NotThrow();
	}

	/// <summary>
	/// Configuration is checked before the database is created, migrated and seeded, so a deployment that cannot
	/// work does not leave one behind on its way out.
	/// </summary>
	[Test]
	public void BrokenConfigurationStopsStartupBeforeAnythingElse()
	{
		using var factory = new CustomWebApplicationFactory(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["Authentication:SigningKey"] = "too-short",
		});

		Action start = () => factory.CreateClient();

		start.Should().Throw<Exception>().Which.Message.Should().Contain("SigningKey");
	}

	/// <summary>
	/// An orchestrator polls this. An instance answering its own probe with 429 for being busy would be restarted
	/// for being busy, so the probe sits outside the limiter.
	/// </summary>
	[Test]
	public async Task TheHealthCheckAnswersEvenOnceTheRateLimitIsSpent()
	{
		using var factory = new CustomWebApplicationFactory(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["RateLimiting:PermitLimit"] = "1",
			["RateLimiting:WindowSeconds"] = "300",
		});

		using var client = factory.CreateClient();

		// Spend the single global permit, then confirm it really is spent.
		await client.GetAsync("/api/auth/me");
		(await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

		for (int probe = 0; probe < 5; probe++)
		{
			(await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
		}
	}
}
