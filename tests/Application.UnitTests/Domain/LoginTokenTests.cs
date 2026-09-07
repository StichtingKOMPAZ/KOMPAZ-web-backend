using FluentAssertions;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using NUnit.Framework;

namespace Kompaz.Application.UnitTests.Domain;

[TestFixture]
internal class LoginTokenTests
{
	private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

	[Test]
	public void AFreshTokenIsRedeemableUntilItExpires()
	{
		var token = LoginToken.Issue(Guid.NewGuid(), "hash", LoginTokenPurpose.MagicLink, Now, TimeSpan.FromMinutes(15));

		token.IsRedeemable(Now.AddMinutes(14)).Should().BeTrue();
		token.IsRedeemable(Now.AddMinutes(16)).Should().BeFalse();
	}

	[Test]
	public void AConsumedTokenCannotBeRedeemedAgain()
	{
		var token = LoginToken.Issue(Guid.NewGuid(), "hash", LoginTokenPurpose.MagicLink, Now, TimeSpan.FromMinutes(15));

		token.Consume(Now.AddMinutes(1));

		token.IsRedeemable(Now.AddMinutes(2)).Should().BeFalse();
	}
}
