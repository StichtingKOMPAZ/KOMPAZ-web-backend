using Kompaz.Domain.Entities;

namespace Kompaz.Application.Common.Interfaces;

public interface IApplicationDbContext
{
	DbSet<Organization> Organizations { get; }

	DbSet<User> Users { get; }

	DbSet<LoginToken> LoginTokens { get; }

	DbSet<RefreshToken> RefreshTokens { get; }

	Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
