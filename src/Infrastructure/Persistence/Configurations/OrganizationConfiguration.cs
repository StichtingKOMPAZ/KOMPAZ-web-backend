using Kompaz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kompaz.Infrastructure.Persistence.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
	public void Configure(EntityTypeBuilder<Organization> builder)
	{
		builder.Property(organization => organization.Name)
			.HasMaxLength(Organization.MaximumNameLength)
			.IsRequired();

		builder.Property(organization => organization.NormalizedName)
			.HasMaxLength(Organization.MaximumNameLength)
			.IsRequired();

		// The unique index is on the folded name, not on the name itself, so two organizations cannot differ by case
		// alone. The name keeps a plain index of its own: it is what the roster orders by.
		builder.HasIndex(organization => organization.NormalizedName)
			.IsUnique();

		builder.HasIndex(organization => organization.Name);

		// At most one organization runs the platform. Nothing over the API sets the flag, so this guards a future
		// mistake rather than a reachable request — and it is what lets a query trust the flag to identify one row.
		builder.HasIndex(organization => organization.IsPlatform)
			.IsUnique()
			.HasFilter("\"IsPlatform\"");

		// The logo goes when the organization does, which is the whole reason the image is a row here rather than a
		// file somewhere a deployment has to remember to clean up.
		builder.HasOne(organization => organization.Logo)
			.WithOne(logo => logo.Organization)
			.HasForeignKey<OrganizationLogo>(logo => logo.OrganizationId)
			.OnDelete(DeleteBehavior.Cascade);

		builder.HasMany(organization => organization.Users)
			.WithOne(user => user.Organization)
			.HasForeignKey(user => user.OrganizationId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}
