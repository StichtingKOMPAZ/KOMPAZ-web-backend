using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kompaz.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
	public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
		: base(options)
	{
	}

	public DbSet<Organization> Organizations => Set<Organization>();

	public DbSet<User> Users => Set<User>();

	public DbSet<LoginToken> LoginTokens => Set<LoginToken>();

	public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
	}
}
