using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kompaz.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
	public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
		: base(options)
	{
	}

	public DbSet<Organization> Organizations => Set<Organization>();

	public DbSet<OrganizationLogo> OrganizationLogos => Set<OrganizationLogo>();

	public DbSet<User> Users => Set<User>();

	public DbSet<LoginToken> LoginTokens => Set<LoginToken>();

	public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

	public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
		Database.BeginTransactionAsync(cancellationToken);

	/// <summary>
	/// Answers a duplicate value with <see cref="ConflictException"/>, so losing the race between a handler's
	/// uniqueness check and its insert produces the same 409 that the check itself would have, rather than a 500.
	/// Handlers still pre-check: that is what produces a message naming the value that clashed. This closes the
	/// window between the two, where only the database can tell.
	/// </summary>
	public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
	{
		try
		{
			return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
		}
		catch (DbUpdateException exception) when (UniqueConstraint.WasViolated(exception))
		{
			throw new ConflictException("Dit verzoek gaat niet samen met een waarde die al bestaat.", exception);
		}
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
	}
}
