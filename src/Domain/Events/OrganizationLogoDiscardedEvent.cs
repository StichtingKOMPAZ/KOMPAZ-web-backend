using Kompaz.Domain.Common;

namespace Kompaz.Domain.Events;

/// <summary>
/// A stored logo is no longer pointed at by anything and its file should go.
/// <para>
/// Raised whenever a row stops naming a key — a replacement, a deleted logo, a deleted organization — and handled
/// after the save, because the file store is outside the database and its transaction. Carries the key as a value
/// for the same reason <see cref="UserDeletedEvent"/> carries an address: by the time this runs the row that knew
/// it may be gone, so a reaction that had to look it up would have nothing to read.
/// </para>
/// </summary>
/// <param name="StorageKey">Where the file is in the store.</param>
public sealed record OrganizationLogoDiscardedEvent(string StorageKey) : DomainEvent;
