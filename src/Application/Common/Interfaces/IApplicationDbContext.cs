using Kompaz.Domain.Entities;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kompaz.Application.Common.Interfaces;

public interface IApplicationDbContext
{
	DbSet<Organization> Organizations { get; }

	DbSet<User> Users { get; }

	DbSet<LoginToken> LoginTokens { get; }

	DbSet<RefreshToken> RefreshTokens { get; }

	Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Opens a transaction so a run of writes becomes visible all at once.
	/// <para>
	/// Rotating a refresh token needs this. Spending the old token and inserting its successor are two statements,
	/// and a concurrent request that finds the old one already spent treats that as a replay and revokes the chain.
	/// Without a transaction it can do so in the gap between the two, revoking a chain that does not contain the
	/// successor yet and leaving it alive. Inside one, the replaying request waits on the row it is trying to spend
	/// and sees the successor once it is there.
	/// </para>
	/// </summary>
	Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
