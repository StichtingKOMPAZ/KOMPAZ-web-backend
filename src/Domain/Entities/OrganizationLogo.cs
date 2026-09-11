using Kompaz.Domain.Common;
using Kompaz.Domain.Events;

namespace Kompaz.Domain.Entities;

/// <summary>
/// The image an organization is shown with. At most one per organization, replaced rather than added to.
/// <para>
/// The row is a pointer, not the picture: the bytes live in the file store and this records where, together with
/// what they were recognized as. Keeping the two apart is what lets the database stay small while the store holds
/// whatever size an upload turns out to be — and it is the shape the next kind of upload needs, so documents will
/// not have to move logos first.
/// </para>
/// <para>
/// <strong>The cost of that split is that a delete is no longer one transaction.</strong> Every path that lets go
/// of an image raises <see cref="OrganizationLogoDiscardedEvent"/> and the store is emptied after the
/// database commits, which means a failure there leaves a file nothing points at rather than a row pointing at
/// nothing. That is the way round it has to be: an orphaned file costs storage and is logged, while an orphaned
/// row would be a broken image on somebody's screen.
/// </para>
/// </summary>
public class OrganizationLogo : AuditableEntity
{
	public Guid OrganizationId { get; set; }

	public Organization Organization { get; set; } = null!;

	/// <summary>
	/// Gets where the bytes are in the file store. Minted here rather than accepted from a caller: it names the
	/// organization and a fresh identifier, so nothing an upload says can reach another organization's file or
	/// overwrite an image this row does not own.
	/// </summary>
	public string StorageKey { get; set; } = string.Empty;

	/// <summary>
	/// Gets the media type the bytes actually are, decided by reading them rather than by believing the upload.
	/// It is served back verbatim, so a value nothing verified would be a media type an attacker chose.
	/// </summary>
	public string ContentType { get; set; } = string.Empty;

	/// <summary>
	/// Gets how large the image is. Recorded because the row is now the only thing that knows anything about the
	/// file without asking the store.
	/// </summary>
	public int ByteCount { get; set; }

	/// <summary>
	/// Mints a key for an organization's logo. A fresh identifier every time, so replacing an image writes a new
	/// file rather than overwriting the one still being served: the old key stays valid until the new row is
	/// committed, and only then is it discarded.
	/// </summary>
	/// <param name="organizationId">Whose logo it is, which prefixes the key.</param>
	/// <param name="extension">The file extension for the format, without a dot.</param>
	public static string MintKey(Guid organizationId, string extension) =>
		$"organizations/{organizationId:D}/logo-{Guid.NewGuid():N}.{extension}";

	public static OrganizationLogo For(Guid organizationId, string storageKey, string contentType, int byteCount) =>
		new()
		{
			OrganizationId = organizationId,
			StorageKey = storageKey,
			ContentType = contentType,
			ByteCount = byteCount,
		};

	/// <summary>
	/// Points this row at a newly stored image and raises the discarding of the one it replaces, so the file the
	/// row no longer names is cleaned up once the change is committed.
	/// </summary>
	public void Replace(string storageKey, string contentType, int byteCount)
	{
		Discard();

		StorageKey = storageKey;
		ContentType = contentType;
		ByteCount = byteCount;
	}

	/// <summary>
	/// Raises the removal of the file this row currently names. Called by whatever is about to stop pointing at
	/// it — a replacement, a deleted logo, or a deleted organization.
	/// </summary>
	public void Discard()
	{
		if (!string.IsNullOrEmpty(StorageKey))
		{
			AddDomainEvent(new OrganizationLogoDiscardedEvent(StorageKey));
		}
	}
}
