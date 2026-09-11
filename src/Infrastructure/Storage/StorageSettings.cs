using System.Text.RegularExpressions;

namespace Kompaz.Infrastructure.Storage;

/// <summary>
/// Strongly typed file-store configuration bound from the "Storage" section.
/// </summary>
internal sealed partial class StorageSettings
{
	public const string SectionName = "Storage";

	/// <summary>
	/// Gets the connection string for the blob account files are kept in. Empty in checked-in settings, like the
	/// database's: a deployment that forgets it should fail at startup rather than run against a default.
	/// <para>
	/// A connection string rather than the managed identity, which would be the better answer and is not
	/// available: granting <c>Storage Blob Data Contributor</c> needs a role assignment that this subscription's
	/// constrained Owner condition forbids — the same reason the container registry uses admin credentials. The
	/// account key is a Key Vault secret resolved by the identity, so nothing is stored in the clear.
	/// </para>
	/// </summary>
	public string ConnectionString { get; init; } = string.Empty;

	/// <summary>
	/// Gets the blob container files go in. Created by <c>infra/foundation.bicep</c>, so this names it rather
	/// than describing it.
	/// </summary>
	public string ContainerName { get; init; } = "organization-logos";

	/// <summary>
	/// Gets the directory the development fallback writes to, or empty to use one under the content root.
	/// </summary>
	public string LocalPath { get; init; } = string.Empty;

	/// <summary>
	/// Gets a value indicating whether there is a real store to reach. Without one, files go to the local
	/// filesystem, which is only acceptable on a developer machine.
	/// </summary>
	public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

	/// <summary>
	/// Reports the first configuration problem that would stop files from being stored.
	/// <para>
	/// The container name is checked here rather than left to the first upload. Azure's rules for it are narrow
	/// and a name that breaks them is rejected by the service, so the symptom of a typo would otherwise be a
	/// logo upload failing in production long after the deployment that caused it looked successful.
	/// </para>
	/// </summary>
	public string? Validate()
	{
		if (!ContainerNamePattern().IsMatch(ContainerName))
		{
			return $"{SectionName}:{nameof(ContainerName)} must be 3 to 63 characters of lower-case letters, "
				+ $"digits and single hyphens, starting and ending with a letter or digit "
				+ $"(current value: \"{ContainerName}\").";
		}

		return null;
	}

	/// <summary>
	/// Azure's own rule for a blob container name.
	/// </summary>
	[GeneratedRegex("^[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9])){1,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
	private static partial Regex ContainerNamePattern();
}
