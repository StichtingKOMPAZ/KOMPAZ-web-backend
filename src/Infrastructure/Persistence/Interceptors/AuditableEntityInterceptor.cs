using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kompaz.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps <see cref="AuditableEntity"/> timestamps, and the caller behind them, as changes are saved.
/// <para>
/// Doing it here rather than in each handler means one clock reading per save instead of one per handler, no way to
/// forget, and no handler taking a <see cref="TimeProvider"/> only to write two fields.
/// </para>
/// </summary>
internal sealed class AuditableEntityInterceptor : SaveChangesInterceptor
{
	private readonly IUser _user;
	private readonly TimeProvider _timeProvider;

	public AuditableEntityInterceptor(IUser user, TimeProvider timeProvider)
	{
		_user = user;
		_timeProvider = timeProvider;
	}

	public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
	{
		Stamp(eventData.Context);

		return base.SavingChanges(eventData, result);
	}

	public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData eventData,
		InterceptionResult<int> result,
		CancellationToken cancellationToken = default)
	{
		Stamp(eventData.Context);

		return base.SavingChangesAsync(eventData, result, cancellationToken);
	}

	private void Stamp(DbContext? context)
	{
		if (context is null)
		{
			return;
		}

		var entries = context.ChangeTracker.Entries<AuditableEntity>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified)
			.ToList();

		if (entries.Count == 0)
		{
			return;
		}

		var now = _timeProvider.GetUtcNow();

		// The timestamps are stamped whether or not anybody is signed in. Plenty of writes here have no caller — the
		// seeder plants the first organization, and redeeming a sign-in link activates a user before they have an
		// access token — and leaving those rows with a default date would be worse than leaving the author unknown.
		var userId = _user.Id;

		foreach (var entry in entries)
		{
			if (entry.State is EntityState.Added)
			{
				entry.Entity.CreatedUtc = now;
				entry.Entity.CreatedBy = userId;
			}

			entry.Entity.UpdatedUtc = now;
			entry.Entity.UpdatedBy = userId;
		}
	}
}
