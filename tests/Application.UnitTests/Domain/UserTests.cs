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

	[Test]
	public void DeletingMarksTheUserWithoutTouchingWhereTheyGotTo()
	{
		var user = User.Invite(Guid.NewGuid(), "iemand@kompaz.local", "Iemand", UserRole.Member, Now);
		user.Activate(Now);

		user.Delete(Now.AddDays(1));

		user.IsDeleted.Should().BeTrue();
		user.DeletedUtc.Should().Be(Now.AddDays(1));
		user.Status.Should().Be(UserStatus.Active);
	}

	/// <summary>
	/// Restoring has to put back what deleting took away and nothing else, which is the reason the deletion is
	/// kept apart from the status rather than being a value of it.
	/// </summary>
	[Test]
	public void RestoringReturnsTheUserToExactlyWhereTheyWere()
	{
		var user = User.Invite(Guid.NewGuid(), "iemand@kompaz.local", "Iemand", UserRole.Member, Now);
		user.Activate(Now);
		user.RecordLogin(Now);
		user.Delete(Now.AddDays(1));

		user.Restore();

		user.IsDeleted.Should().BeFalse();
		user.DeletedUtc.Should().BeNull();
		user.Status.Should().Be(UserStatus.Active);
		user.ActivatedUtc.Should().Be(Now);
		user.LastLoginUtc.Should().Be(Now);
	}

	/// <summary>
	/// Re-inviting a deleted address reuses their row, so the new name and role apply and they owe an acceptance
	/// again — but what already happened to them is history and stays.
	/// </summary>
	[Test]
	public void RevivingAsInvitedTakesTheNewDetailsAndKeepsTheHistory()
	{
		var user = User.Invite(Guid.NewGuid(), "iemand@kompaz.local", "Iemand", UserRole.Member, Now);
		user.Activate(Now);
		user.RecordLogin(Now);
		user.Delete(Now.AddDays(1));

		user.ReviveAsInvited("Andere Naam", UserRole.Administrator, Now.AddDays(2));

		user.IsDeleted.Should().BeFalse();
		user.Status.Should().Be(UserStatus.Invited);
		user.Name.Should().Be("Andere Naam");
		user.Role.Should().Be(UserRole.Administrator);
		user.InvitedUtc.Should().Be(Now.AddDays(2));
		user.ActivatedUtc.Should().Be(Now);
		user.LastLoginUtc.Should().Be(Now);
	}
}
