using Kompaz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kompaz.Infrastructure.Persistence.Configurations;

public class LoginTokenConfiguration : IEntityTypeConfiguration<LoginToken>
{
	public void Configure(EntityTypeBuilder<LoginToken> builder)
	{
		builder.Property(token => token.TokenHash)
			.HasMaxLength(128)
			.IsRequired();

		builder.Property(token => token.Purpose)
			.HasConversion<string>()
			.HasMaxLength(50)
			.IsRequired();

		builder.HasIndex(token => token.TokenHash)
			.IsUnique();

		builder.HasIndex(token => new { token.UserId, token.ConsumedUtc });
	}
}
