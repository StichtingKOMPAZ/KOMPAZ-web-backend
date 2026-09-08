using MediatR;

namespace Kompaz.Domain.Common;

/// <summary>
/// Something that happened to an entity, which parts of the system other than the one that caused it may need to
/// react to. Raised with <see cref="Entity.AddDomainEvent"/> and published when the change is saved.
/// </summary>
public abstract record DomainEvent : INotification;
