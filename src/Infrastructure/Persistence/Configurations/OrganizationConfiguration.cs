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

		builder.HasMany(organization => organization.Users)
			.WithOne(user => user.Organization)
			.HasForeignKey(user => user.OrganizationId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}
