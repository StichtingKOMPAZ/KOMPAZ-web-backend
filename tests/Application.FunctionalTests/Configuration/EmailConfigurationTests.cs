using FluentAssertions;
using Kompaz.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Kompaz.Application.FunctionalTests.Configuration;

/// <summary>
/// The fallback email sink writes sign-in links to the log. These cover the rule that keeps it on a developer
/// machine: without a relay, anything but Development refuses to wire itself up at all.
/// </summary>
[TestFixture]
internal sealed class EmailConfigurationTests
{
	[Test]
	public void AMissingRelayIsRefusedOutsideDevelopment()
	{
		var register = () => new ServiceCollection()
			.AddInfrastructureServices(ConfigurationWithoutSmtp(), EnvironmentNamed(Environments.Production));

		register.Should().Throw<InvalidOperationException>()
			.WithMessage("*Smtp must be configured*")
			.Which.Message.Should().Contain("written to the application log");
	}

	[Test]
	public void AMissingRelayIsRefusedInStagingToo()
	{
		var register = () => new ServiceCollection()
			.AddInfrastructureServices(ConfigurationWithoutSmtp(), EnvironmentNamed(Environments.Staging));

		register.Should().Throw<InvalidOperationException>();
	}

	[Test]
	public void AMissingRelayFallsBackToTheLogInDevelopment()
	{
		var register = () => new ServiceCollection()
			.AddInfrastructureServices(ConfigurationWithoutSmtp(), EnvironmentNamed(Environments.Development));

		register.Should().NotThrow();
	}

	/// <summary>
	/// Production with a relay configured. The blob account is configured alongside it because this is about the
	/// email rule: the storage rule refuses Production too, and leaving it out would have this test passing or
	/// failing for the wrong reason.
	/// </summary>
	[Test]
	public void AConfiguredRelayIsAcceptedAnywhere()
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Email:Smtp:Host"] = "smtp.example.com",
				["Email:Smtp:UserName"] = "kompaz",
				["Email:Smtp:Password"] = "secret",
				["Storage:ConnectionString"] = StorageConfigurationTests.ConnectionString,
			})
			.Build();

		var register = () => new ServiceCollection()
			.AddInfrastructureServices(configuration, EnvironmentNamed(Environments.Production));

		register.Should().NotThrow();
	}

	private static IConfiguration ConfigurationWithoutSmtp() =>
		new ConfigurationBuilder().Build();

	private static StubHostEnvironment EnvironmentNamed(string name) => StubHostEnvironment.Named(name);
}
