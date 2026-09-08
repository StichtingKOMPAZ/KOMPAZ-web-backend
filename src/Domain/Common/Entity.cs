using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Kompaz.Domain.Common;

/// <summary>
/// The base for everything this application stores: an identity, and the events raised while changing it.
/// <para>
/// <see cref="Id"/> is deliberately left unset rather than defaulted to <see cref="Guid.NewGuid"/>. EF Core fills it
/// in on <c>Add</c> with a sequential value, which keeps inserts at the end of the primary-key index instead of
/// scattering them; a random identifier assigned here would take that away.
/// </para>
/// </summary>
public abstract class Entity
{
	private readonly List<DomainEvent> _domainEvents = [];

	public Guid Id { get; set; }

	/// <summary>
	/// Gets what happened to this entity that the rest of the system may need to react to. Published when the change
	/// is saved, so a reaction cannot fire for something that was never persisted.
	/// </summary>
	[NotMapped]
	[JsonIgnore]
	public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

	public void AddDomainEvent(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);

	public void ClearDomainEvents() => _domainEvents.Clear();
}
