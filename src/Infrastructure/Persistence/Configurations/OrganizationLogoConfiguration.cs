using Kompaz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kompaz.Infrastructure.Persistence.Configurations;

public class OrganizationLogoConfiguration : IEntityTypeConfiguration<OrganizationLogo>
{
	public void Configure(EntityTypeBuilder<OrganizationLogo> builder)
	{
		builder.Property(logo => logo.StorageKey)
			.HasMaxLength(512)
			.IsRequired();

		builder.Property(logo => logo.ContentType)
			.HasMaxLength(100)
			.IsRequired();

		// One logo per organization, enforced rather than assumed: the upload replaces the row it finds, and two
		// rows would make "the logo" a question about ordering.
		builder.HasIndex(logo => logo.OrganizationId)
			.IsUnique();
	}
}
