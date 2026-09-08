using Kompaz.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kompaz.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Publishes the domain events an entity raised, once the change that raised them has been written.
/// <para>
/// <strong>After the save, not before it.</strong> The reactions here reach outside the process — an email — and
/// holding a database transaction open across an SMTP conversation would let one slow relay block every writer for
/// as long as its timeout. The cost is that a reaction can fail after the data is committed, which is why the
/// commands that raise these events are safe to repeat: re-inviting somebody who has not accepted sends the link
/// again rather than refusing the address.
/// </para>
/// </summary>
internal sealed class DispatchDomainEventsInterceptor : SaveChangesInterceptor
{
	private readonly IPublisher _publisher;

	public DispatchDomainEventsInterceptor(IPublisher publisher)
	{
		_publisher = publisher;
	}

	public override async ValueTask<int> SavedChangesAsync(
		SaveChangesCompletedEventData eventData,
		int result,
		CancellationToken cancellationToken = default)
	{
		await PublishAsync(eventData.Context, cancellationToken);

		return await base.SavedChangesAsync(eventData, result, cancellationToken);
	}

	public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
	{
		PublishAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();

		return base.SavedChanges(eventData, result);
	}

	private async Task PublishAsync(DbContext? context, CancellationToken cancellationToken)
	{
		if (context is null)
		{
			return;
		}

		var raised = context.ChangeTracker.Entries<Entity>()
			.Select(entry => entry.Entity)
			.Where(entity => entity.DomainEvents.Count > 0)
			.ToList();

		if (raised.Count == 0)
		{
			return;
		}

		var events = raised.SelectMany(entity => entity.DomainEvents).ToList();

		// Cleared before publishing, not after: a handler that saves again would otherwise find the same events
		// still attached and publish them a second time.
		raised.ForEach(entity => entity.ClearDomainEvents());

		foreach (var domainEvent in events)
		{
			await _publisher.Publish(domainEvent, cancellationToken);
		}
	}
}
