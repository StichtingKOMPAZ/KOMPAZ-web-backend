using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Events;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Kompaz.Application.Organizations.EventHandlers;

/// <summary>
/// Removes the file behind a logo nothing points at any more.
/// </summary>
internal sealed class DeleteDiscardedLogoHandler : INotificationHandler<OrganizationLogoDiscardedEvent>
{
	private readonly IFileStore _files;
	private readonly ILogger<DeleteDiscardedLogoHandler> _logger;

	public DeleteDiscardedLogoHandler(IFileStore files, ILogger<DeleteDiscardedLogoHandler> logger)
	{
		_files = files;
		_logger = logger;
	}

	/// <summary>
	/// Deletes the file, and reports rather than rethrows when the store will not take the request.
	/// <para>
	/// This is the reconciliation the database no longer does for us, and it runs after the commit — so the row
	/// is already gone and there is nothing to answer the caller differently about. An exception here would turn
	/// a successful deletion into a 500, and the caller's obvious response, deleting again, now returns 404 while
	/// the file is still there. Logged with the key instead, which is what a sweep would need to find it.
	/// </para>
	/// </summary>
	[SuppressMessage(
		"Design",
		"CA1031:Do not catch general exception types",
		Justification = "The database change is already committed; a storage failure must not report it as failed.")]
	[SuppressMessage(
		"Major Code Smell",
		"S2221:\"Exception\" should not be caught when not required by called methods",
		Justification = "The database change is already committed; a storage failure must not report it as failed.")]
	public async Task Handle(OrganizationLogoDiscardedEvent notification, CancellationToken cancellationToken)
	{
		try
		{
			await _files.DeleteAsync(notification.StorageKey, cancellationToken);
		}
		catch (Exception exception)
		{
			_logger.LogError(
				exception,
				"Failed to remove the stored logo {StorageKey}, which nothing refers to any more. It is orphaned.",
				notification.StorageKey);
		}
	}
}
