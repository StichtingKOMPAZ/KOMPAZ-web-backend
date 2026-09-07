using Kompaz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kompaz.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
	public void Configure(EntityTypeBuilder<User> builder)
	{
		builder.Property(user => user.Email)
			.HasMaxLength(320)
			.IsRequired();

		builder.Property(user => user.NormalizedEmail)
			.HasMaxLength(320)
			.IsRequired();

		builder.Property(user => user.Name)
			.HasMaxLength(200)
			.IsRequired();

		builder.Property(user => user.Role)
			.HasConversion<string>()
			.HasMaxLength(50)
			.IsRequired();

		builder.Property(user => user.Status)
			.HasConversion<string>()
			.HasMaxLength(50)
			.IsRequired();

		builder.HasIndex(user => user.NormalizedEmail)
			.IsUnique();

		builder.HasIndex(user => new { user.OrganizationId, user.Status });

		builder.HasMany(user => user.LoginTokens)
			.WithOne(token => token.User)
			.HasForeignKey(token => token.UserId)
			.OnDelete(DeleteBehavior.Cascade);

		builder.HasMany(user => user.RefreshTokens)
			.WithOne(token => token.User)
			.HasForeignKey(token => token.UserId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}
