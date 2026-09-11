using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Kompaz.Application.FunctionalTests.Configuration;

/// <summary>
/// A host environment with nothing but a name, for the registration rules that turn on which environment they are
/// in. Shared, because more than one of those rules exists and each one needs to be asked about Development,
/// Staging and Production in turn.
/// </summary>
internal sealed class StubHostEnvironment : IHostEnvironment
{
	public string EnvironmentName { get; set; } = Environments.Production;

	public string ApplicationName { get; set; } = "Kompaz.Tests";

	public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

	public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

	public static StubHostEnvironment Named(string name) => new() { EnvironmentName = name };
}
