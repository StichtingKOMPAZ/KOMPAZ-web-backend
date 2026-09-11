using FluentAssertions;
using Kompaz.Infrastructure.Storage;
using NUnit.Framework;

namespace Kompaz.Application.UnitTests.Storage;

/// <summary>
/// Azure's rules for a blob container name are narrow and the service is what enforces them, so a typo would
/// otherwise deploy cleanly and fail on the first logo somebody uploaded. Checked at startup instead.
/// </summary>
[TestFixture]
internal sealed class StorageSettingsTests
{
	[TestCase("organization-logos")]
	[TestCase("logos")]
	[TestCase("a1b")]
	[TestCase("kompaz-organization-logos-2026")]
	public void ANameTheServiceAcceptsPasses(string containerName)
	{
		new StorageSettings { ContainerName = containerName }.Validate().Should().BeNull();
	}

	[TestCase("Organization-Logos", TestName = "an upper-case letter")]
	[TestCase("ab", TestName = "shorter than three characters")]
	[TestCase("organization--logos", TestName = "two hyphens together")]
	[TestCase("-organization-logos", TestName = "a leading hyphen")]
	[TestCase("organization-logos-", TestName = "a trailing hyphen")]
	[TestCase("organization_logos", TestName = "an underscore")]
	[TestCase("", TestName = "nothing at all")]
	public void ANameTheServiceWouldRejectIsReported(string containerName)
	{
		new StorageSettings { ContainerName = containerName }.Validate()
			.Should().NotBeNull().And.Contain("ContainerName");
	}

	/// <summary>
	/// The switch between the blob store and the development fallback. Only the connection string decides it: a
	/// container name alone reaches nothing.
	/// </summary>
	[Test]
	public void AStoreIsOnlyConfiguredOnceThereIsAConnectionString()
	{
		new StorageSettings().IsConfigured.Should().BeFalse();
		new StorageSettings { ConnectionString = "   " }.IsConfigured.Should().BeFalse();
		new StorageSettings { ConnectionString = "UseDevelopmentStorage=true" }.IsConfigured.Should().BeTrue();
	}
}
