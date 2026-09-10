using Kompaz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kompaz.Infrastructure.Persistence.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
	public void Configure(EntityTypeBuilder<Organization> builder)
	{
		builder.Property(organization => organization.Name)
			.HasMaxLength(200)
			.IsRequired();

		builder.HasIndex(organization => organization.Name)
			.IsUnique();

		// At most one organization runs the platform. Nothing over the API sets the flag, so this guards a future
		// mistake rather than a reachable request — and it is what lets a query trust the flag to identify one row.
		builder.HasIndex(organization => organization.IsPlatform)
			.IsUnique()
			.HasFilter("\"IsPlatform\"");

		builder.HasMany(organization => organization.Users)
			.WithOne(user => user.Organization)
			.HasForeignKey(user => user.OrganizationId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}
