using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Kompaz.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kompaz.Infrastructure.Storage;

/// <summary>
/// Keeps uploaded files in an Azure blob container.
/// <para>
/// A singleton, because <see cref="BlobContainerClient"/> is thread-safe and holds the pooled HTTP connections
/// underneath — one per process rather than one per request, which is the whole reason the Azure SDK clients are
/// built to be reused.
/// </para>
/// </summary>
internal sealed class BlobFileStore : IFileStore
{
	private readonly BlobContainerClient _container;
	private readonly ILogger<BlobFileStore> _logger;

	public BlobFileStore(IOptions<StorageSettings> settings, ILogger<BlobFileStore> logger)
	{
		var value = settings.Value;

		_container = new BlobServiceClient(value.ConnectionString).GetBlobContainerClient(value.ContainerName);
		_logger = logger;
	}

	public async Task SaveAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
	{
		var blob = _container.GetBlobClient(key);
		var headers = new BlobHttpHeaders { ContentType = contentType };

		try
		{
			await UploadAsync(blob, content, headers, cancellationToken);
		}
		catch (RequestFailedException exception) when (exception.ErrorCode == BlobErrorCode.ContainerNotFound)
		{
			// The container is created by infra/foundation.bicep, so reaching this means either a local emulator
			// nobody has set up or a container that has been removed. Created and retried once rather than
			// failing: the alternative is an upload that dies with a message about a container the person
			// uploading a logo has never heard of.
			_logger.LogWarning(
				"Blob container {ContainerName} did not exist and was created. It should be created by the "
				+ "infrastructure deployment, so this is worth looking into.",
				_container.Name);

			await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
			await UploadAsync(blob, content, headers, cancellationToken);
		}
	}

	public async Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
	{
		try
		{
			var response = await _container.GetBlobClient(key).DownloadContentAsync(cancellationToken);

			return response.Value.Content.ToArray();
		}
		catch (RequestFailedException exception) when (exception.Status == 404)
		{
			// Both "no such blob" and "no such container" arrive here, and both mean the same thing to a caller:
			// there is nothing to serve. Answered with null so it shows the placeholder — see IFileStore.
			return null;
		}
	}

	public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		// Already gone is the outcome this was asked for, and the overload that says so avoids an exception on the
		// path that cleans up after a failed cleanup.
		await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);
	}

	private static async Task UploadAsync(
		BlobClient blob,
		byte[] content,
		BlobHttpHeaders headers,
		CancellationToken cancellationToken)
	{
		using var stream = new MemoryStream(content, writable: false);

		await blob.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = headers }, cancellationToken);
	}
}
