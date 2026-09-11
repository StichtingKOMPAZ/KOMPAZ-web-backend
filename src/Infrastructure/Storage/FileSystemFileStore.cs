using Kompaz.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Kompaz.Infrastructure.Storage;

/// <summary>
/// Keeps uploaded files in a directory on disk. Selected while no blob account is configured, so the API is usable
/// straight from a fresh checkout without an emulator.
/// <para>
/// This must never be the active store outside development. A container app's filesystem is ephemeral: every
/// revision, restart and scale-out starts with an empty one, so the logos would quietly disappear and, with more
/// than one replica, would be visible only to whichever replica happened to receive the upload.
/// </para>
/// </summary>
internal sealed class FileSystemFileStore : IFileStore
{
	private readonly string _root;

	public FileSystemFileStore(string root, ILogger<FileSystemFileStore> logger)
	{
		_root = Path.GetFullPath(root);

		logger.LogWarning(
			"No blob account is configured, keeping uploaded files under {Root}. This is for development only: a "
			+ "deployment's filesystem does not survive a restart.",
			_root);
	}

	public async Task SaveAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
	{
		string path = Resolve(key);

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		// The media type is not written anywhere. The row that names this file records it, and inventing a sidecar
		// to hold a second copy would only be a second thing that can disagree.
		await File.WriteAllBytesAsync(path, content, cancellationToken);
	}

	public async Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
	{
		string path = Resolve(key);

		return File.Exists(path)
			? await File.ReadAllBytesAsync(path, cancellationToken)
			: null;
	}

	public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		File.Delete(Resolve(key));

		return Task.CompletedTask;
	}

	/// <summary>
	/// Turns a store key into a path under the root, refusing anything that would climb out of it.
	/// <para>
	/// Keys are minted by this application and read back from its own database, so nothing a caller sends reaches
	/// here today. It is checked anyway: this is the one implementation where a key is a path, and "the value in
	/// that column was always written by us" is the kind of assumption that stops being true one feature later —
	/// at which point the failure is reading and writing arbitrary files on the host.
	/// </para>
	/// </summary>
	private string Resolve(string key)
	{
		if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key))
		{
			throw new ArgumentException($"\"{key}\" is not a relative store key.", nameof(key));
		}

		string path = Path.GetFullPath(Path.Combine(_root, key));

		// Compared against the root with a trailing separator, so a sibling directory whose name merely starts
		// with the root's does not count as being inside it.
		string root = _root.EndsWith(Path.DirectorySeparatorChar)
			? _root
			: _root + Path.DirectorySeparatorChar;

		if (!path.StartsWith(root, StringComparison.Ordinal))
		{
			throw new ArgumentException($"\"{key}\" resolves outside the file store.", nameof(key));
		}

		return path;
	}
}
