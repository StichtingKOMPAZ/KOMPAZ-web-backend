using FluentAssertions;
using Kompaz.Domain.Entities;
using NUnit.Framework;

namespace Kompaz.Application.UnitTests.Domain;

[TestFixture]
internal class RefreshTokenTests
{
	private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
	private static readonly TimeSpan Sliding = TimeSpan.FromDays(14);
	private static readonly TimeSpan Absolute = TimeSpan.FromDays(90);

	[Test]
	public void ANewSessionExpiresAfterTheSlidingWindowAndCapsAtTheAbsoluteOne()
	{
		var token = RefreshToken.StartSession(Guid.NewGuid(), "hash", Now, Sliding, Absolute);

		token.ExpiresUtc.Should().Be(Now.Add(Sliding));
		token.AbsoluteExpiresUtc.Should().Be(Now.Add(Absolute));
		token.IsSpent.Should().BeFalse();
	}

	[Test]
	public void RotatingSlidesTheWindowForwardFromTheMomentOfUse()
	{
		var token = RefreshToken.StartSession(Guid.NewGuid(), "hash", Now, Sliding, Absolute);
		var later = Now.AddDays(10);

		var successor = token.Rotate("successor-hash", later, Sliding);

		successor.ExpiresUtc.Should().Be(later.Add(Sliding));
		successor.ExpiresUtc.Should().BeAfter(token.ExpiresUtc);
	}

	[Test]
	public void RotatingKeepsTheSessionAndItsAbsoluteDeadline()
	{
		var token = RefreshToken.StartSession(Guid.NewGuid(), "hash", Now, Sliding, Absolute);

		var successor = token.Rotate("successor-hash", Now.AddDays(10), Sliding);

		successor.SessionId.Should().Be(token.SessionId);
		successor.UserId.Should().Be(token.UserId);
		successor.AbsoluteExpiresUtc.Should().Be(token.AbsoluteExpiresUtc);
	}

	[Test]
	public void TheSlidingWindowNeverReachesPastTheAbsoluteDeadline()
	{
		var token = RefreshToken.StartSession(Guid.NewGuid(), "hash", Now, Sliding, Absolute);
		var nearTheEnd = Now.AddDays(85);

		var successor = token.Rotate("successor-hash", nearTheEnd, Sliding);

		successor.ExpiresUtc.Should().Be(token.AbsoluteExpiresUtc);
		successor.ExpiresUtc.Should().BeBefore(nearTheEnd.Add(Sliding));
	}

	[Test]
	public void ASessionShorterThanItsSlidingWindowStillCapsAtTheAbsoluteDeadline()
	{
		var token = RefreshToken.StartSession(Guid.NewGuid(), "hash", Now, Sliding, TimeSpan.FromDays(3));

		token.ExpiresUtc.Should().Be(Now.AddDays(3));
	}

	[Test]
	public void RotatingSpendsTheTokenItReplaces()
	{
		var token = RefreshToken.StartSession(Guid.NewGuid(), "hash", Now, Sliding, Absolute);

		token.Rotate("successor-hash", Now.AddDays(1), Sliding);

		token.IsSpent.Should().BeTrue();
		token.ConsumedUtc.Should().Be(Now.AddDays(1));
		token.IsRedeemable(Now.AddDays(1)).Should().BeFalse();
	}

	[Test]
	public void ARevokedTokenIsNoLongerRedeemable()
	{
		var token = RefreshToken.StartSession(Guid.NewGuid(), "hash", Now, Sliding, Absolute);

		token.Revoke(Now.AddDays(1));

		token.IsSpent.Should().BeTrue();
		token.IsRedeemable(Now.AddDays(2)).Should().BeFalse();
	}

	[Test]
	public void AnIdleTokenLapsesAfterTheSlidingWindow()
	{
		var token = RefreshToken.StartSession(Guid.NewGuid(), "hash", Now, Sliding, Absolute);

		token.IsRedeemable(Now.AddDays(13)).Should().BeTrue();
		token.IsRedeemable(Now.AddDays(15)).Should().BeFalse();
	}

	[Test]
	public void SpendingATokenTwiceKeepsTheFirstMoment()
	{
		var token = RefreshToken.StartSession(Guid.NewGuid(), "hash", Now, Sliding, Absolute);

		token.Consume(Now.AddDays(1));
		token.Consume(Now.AddDays(2));

		token.ConsumedUtc.Should().Be(Now.AddDays(1));
	}
}
