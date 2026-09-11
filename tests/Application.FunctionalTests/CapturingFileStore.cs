using Kompaz.Application.Common.Interfaces;
using System.Collections.Concurrent;

namespace Kompaz.Application.FunctionalTests;

/// <summary>
/// Stands in for the blob container and keeps uploaded files in memory, so a test can check what was stored and
/// what was cleaned up afterwards.
/// <para>
/// Replaced for the same reason email is: it is the outside world. The database is not — it is the real provider
/// against a real server — because what a test needs to know about persistence is what PostgreSQL will actually
/// do, whereas what it needs to know about the file store is only which keys it holds.
/// </para>
/// </summary>
internal sealed class CapturingFileStore : IFileStore
{
	private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

	/// <summary>
	/// Gets or sets a value indicating whether deleting fails, the way a container that has gone away does. Set it
	/// to reach the behaviour that a failed cleanup must not be visible to the caller.
	/// </summary>
	public bool DeletionFails { get; set; }

	/// <summary>
	/// Gets how many files are held. The point of a test that reads this is that discarding one removes it, so a
	/// replaced or deleted logo does not sit in the store for ever.
	/// </summary>
	public int Count => _files.Count;

	/// <summary>
	/// Gets every key currently held, for a test that wants to name the one it expects.
	/// </summary>
	public IReadOnlyCollection<string> Keys => _files.Keys.ToArray();

	public Task SaveAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
	{
		_files[key] = content;

		return Task.CompletedTask;
	}

	public Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
		Task.FromResult(_files.TryGetValue(key, out byte[]? content) ? content : null);

	public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		if (DeletionFails)
		{
			throw new InvalidOperationException("The container refused the request.");
		}

		_files.TryRemove(key, out _);

		return Task.CompletedTask;
	}

	/// <summary>
	/// Whether a key is held. Takes the key from the row that names it, so a test asserts about the file the
	/// application actually pointed at rather than about one it guessed the name of.
	/// </summary>
	public bool Holds(string key) => _files.ContainsKey(key);
}
