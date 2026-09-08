using FluentAssertions;
using Kompaz.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
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

	[Test]
	public void AConfiguredRelayIsAcceptedAnywhere()
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Email:Smtp:Host"] = "smtp.example.com",
				["Email:Smtp:UserName"] = "kompaz",
				["Email:Smtp:Password"] = "secret",
			})
			.Build();

		var register = () => new ServiceCollection()
			.AddInfrastructureServices(configuration, EnvironmentNamed(Environments.Production));

		register.Should().NotThrow();
	}

	private static IConfiguration ConfigurationWithoutSmtp() =>
		new ConfigurationBuilder().Build();

	private static StubHostEnvironment EnvironmentNamed(string name) =>
		new StubHostEnvironment { EnvironmentName = name };

	private sealed class StubHostEnvironment : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = Environments.Production;

		public string ApplicationName { get; set; } = "Kompaz.Tests";

		public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}
}
