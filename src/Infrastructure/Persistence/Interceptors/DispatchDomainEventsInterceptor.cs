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
/// <para>
/// <strong>Collected before the save, published after it.</strong> Which entities raised something is noted while
/// they are all still tracked, because saving a deletion detaches the row that was deleted: an entity asked for
/// its events afterwards would no longer be there to ask, and the notices a deletion raises — telling the people
/// in a removed organization that their accounts are gone — would be dropped without a word. The events
/// themselves are read and cleared at publishing time, so a save that throws leaves them attached for the retry
/// rather than swallowing them.
/// </para>
/// </summary>
internal sealed class DispatchDomainEventsInterceptor : SaveChangesInterceptor
{
	private readonly IPublisher _publisher;

	/// <summary>
	/// The entities noted by the save currently in flight. Safe as a field because this interceptor is scoped
	/// alongside the context it serves, and because the list is replaced at the start of every save and emptied by
	/// the end of it — so a second save cannot find the first one's entries.
	/// </summary>
	private List<Entity> _raisedBy = [];

	public DispatchDomainEventsInterceptor(IPublisher publisher)
	{
		_publisher = publisher;
	}

	public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
	{
		_raisedBy = Collect(eventData.Context);

		return base.SavingChanges(eventData, result);
	}

	public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData eventData,
		InterceptionResult<int> result,
		CancellationToken cancellationToken = default)
	{
		_raisedBy = Collect(eventData.Context);

		return base.SavingChangesAsync(eventData, result, cancellationToken);
	}

	public override async ValueTask<int> SavedChangesAsync(
		SaveChangesCompletedEventData eventData,
		int result,
		CancellationToken cancellationToken = default)
	{
		await PublishAsync(cancellationToken);

		return await base.SavedChangesAsync(eventData, result, cancellationToken);
	}

	public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
	{
		PublishAsync(CancellationToken.None).GetAwaiter().GetResult();

		return base.SavedChanges(eventData, result);
	}

	/// <summary>
	/// Forgets what the failed save had noted. The events stay on their entities, so the next attempt collects
	/// them again; keeping the list would instead publish them on the back of some later, unrelated save.
	/// </summary>
	public override void SaveChangesFailed(DbContextErrorEventData eventData)
	{
		_raisedBy = [];

		base.SaveChangesFailed(eventData);
	}

	public override Task SaveChangesFailedAsync(
		DbContextErrorEventData eventData,
		CancellationToken cancellationToken = default)
	{
		_raisedBy = [];

		return base.SaveChangesFailedAsync(eventData, cancellationToken);
	}

	private static List<Entity> Collect(DbContext? context) =>
		context is null
			? []
			: [.. context.ChangeTracker.Entries<Entity>()
				.Select(entry => entry.Entity)
				.Where(entity => entity.DomainEvents.Count > 0)];

	private async Task PublishAsync(CancellationToken cancellationToken)
	{
		var raised = _raisedBy;
		_raisedBy = [];

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
