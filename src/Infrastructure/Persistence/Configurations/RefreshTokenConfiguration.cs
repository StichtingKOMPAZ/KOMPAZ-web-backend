using Kompaz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kompaz.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
	public void Configure(EntityTypeBuilder<RefreshToken> builder)
	{
		builder.Property(token => token.TokenHash)
			.HasMaxLength(128)
			.IsRequired();

		builder.Property(token => token.SessionId)
			.IsRequired();

		builder.HasIndex(token => token.TokenHash)
			.IsUnique();

		// Revoking a session walks every token in the chain, so the chain is the index.
		builder.HasIndex(token => token.SessionId);

		builder.Ignore(token => token.IsSpent);
	}
}
