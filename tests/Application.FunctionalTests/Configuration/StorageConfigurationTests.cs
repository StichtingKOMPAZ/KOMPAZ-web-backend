using FluentAssertions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Kompaz.Application.FunctionalTests.Configuration;

/// <summary>
/// The fallback file store keeps uploads on the local filesystem, which a deployment throws away on every
/// restart. These cover the rule that keeps it on a developer machine, and the one that catches a container name
/// the service would reject — both at registration, because the alternative is finding out at the first upload.
/// </summary>
[TestFixture]
internal sealed class StorageConfigurationTests
{
	/// <summary>
	/// A connection string in the shape the SDK parses, pointing nowhere. Nothing here connects: registration
	/// only decides which implementation to wire up.
	/// </summary>
	internal const string ConnectionString =
		"DefaultEndpointsProtocol=https;AccountName=kompaz;AccountKey=a2V5;EndpointSuffix=core.windows.net";

	[Test]
	public void AMissingBlobAccountIsRefusedOutsideDevelopment()
	{
		var register = () => new ServiceCollection()
			.AddInfrastructureServices(WithSmtp(), StubHostEnvironment.Named(Environments.Production));

		register.Should().Throw<InvalidOperationException>()
			.WithMessage("*Storage:ConnectionString must be configured*")
			.Which.Message.Should().Contain("does not survive a restart");
	}

	[Test]
	public void AMissingBlobAccountIsRefusedInStagingToo()
	{
		var register = () => new ServiceCollection()
			.AddInfrastructureServices(WithSmtp(), StubHostEnvironment.Named(Environments.Staging));

		register.Should().Throw<InvalidOperationException>();
	}

	[Test]
	public void AMissingBlobAccountFallsBackToTheFilesystemInDevelopment()
	{
		var services = new ServiceCollection();

		var register = () => services.AddInfrastructureServices(
			new ConfigurationBuilder().Build(), StubHostEnvironment.Named(Environments.Development));

		register.Should().NotThrow();
		services.Should().Contain(service => service.ServiceType == typeof(IFileStore));
	}

	[Test]
	public void AConfiguredBlobAccountIsAcceptedAnywhere()
	{
		var register = () => new ServiceCollection()
			.AddInfrastructureServices(WithSmtp(ConnectionString), StubHostEnvironment.Named(Environments.Production));

		register.Should().NotThrow();
	}

	/// <summary>
	/// A configuration that satisfies the email rule, so a test about storage fails for storage reasons.
	/// </summary>
	private static IConfiguration WithSmtp(string? storageConnectionString = null) =>
		new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Email:Smtp:Host"] = "smtp.example.com",
				["Email:Smtp:UserName"] = "kompaz",
				["Email:Smtp:Password"] = "secret",
				["Storage:ConnectionString"] = storageConnectionString,
			})
			.Build();
}
