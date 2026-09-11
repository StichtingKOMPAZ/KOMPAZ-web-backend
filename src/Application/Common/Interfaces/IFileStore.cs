namespace Kompaz.Application.Common.Interfaces;

/// <summary>
/// Where uploaded files live. Keyed rather than pathed: a key is opaque to this layer, and which of a bucket, a
/// container or a directory it lands in is the infrastructure's business.
/// <para>
/// Bytes rather than streams, because the one caller reads a whole small image and has to look at it twice — once
/// to recognize the format and once to store it. A file large enough to stream is the point to add an overload,
/// not a reason to make every caller manage a stream's lifetime today.
/// </para>
/// </summary>
public interface IFileStore
{
	/// <summary>
	/// Writes a file, replacing whatever is already at the key.
	/// </summary>
	/// <param name="key">Where to put it.</param>
	/// <param name="content">The bytes.</param>
	/// <param name="contentType">The media type to record with them, so a store that serves files directly one
	/// day labels them correctly without this application being asked again.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	Task SaveAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads a file, or returns <see langword="null"/> when the key holds nothing.
	/// <para>
	/// Null rather than an exception, because a key that points at nothing is a state this design allows: the row
	/// naming it is committed before its file is written on a first upload, and cleaning up after a deletion can
	/// fail. A caller that shows a placeholder for a missing image is doing the right thing with it.
	/// </para>
	/// </summary>
	Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes a file. Removing one that is not there is a success: the point is that it is gone.
	/// </summary>
	Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
