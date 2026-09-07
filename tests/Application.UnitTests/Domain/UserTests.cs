using FluentAssertions;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using NUnit.Framework;

namespace Kompaz.Application.UnitTests.Domain;

[TestFixture]
internal class UserTests
{
	private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

	[Test]
	public void AnInvitedUserStartsWithoutAnActivationMoment()
	{
		var user = User.Invite(Guid.NewGuid(), " Iemand@Kompaz.Local ", " Iemand ", UserRole.Member, Now);

		user.Status.Should().Be(UserStatus.Invited);
		user.Email.Should().Be("Iemand@Kompaz.Local");
		user.NormalizedEmail.Should().Be("IEMAND@KOMPAZ.LOCAL");
		user.Name.Should().Be("Iemand");
		user.InvitedUtc.Should().Be(Now);
		user.ActivatedUtc.Should().BeNull();
	}

	[Test]
	public void ActivatingRecordsTheMomentTheInvitationWasAccepted()
	{
		var user = User.Invite(Guid.NewGuid(), "iemand@kompaz.local", "Iemand", UserRole.Member, Now);

		user.Activate(Now.AddDays(1));

		user.Status.Should().Be(UserStatus.Active);
		user.ActivatedUtc.Should().Be(Now.AddDays(1));
	}

	[Test]
	public void ActivatingAnActiveUserKeepsTheOriginalMoment()
	{
		var user = User.Invite(Guid.NewGuid(), "iemand@kompaz.local", "Iemand", UserRole.Member, Now);
		user.Activate(Now.AddDays(1));

		user.Activate(Now.AddDays(2));

		user.ActivatedUtc.Should().Be(Now.AddDays(1));
	}
}
