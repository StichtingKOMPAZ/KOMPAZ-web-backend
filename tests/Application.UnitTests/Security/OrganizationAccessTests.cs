using FluentAssertions;
using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using NUnit.Framework;

namespace Kompaz.Application.UnitTests.Security;

[TestFixture]
internal class OrganizationAccessTests
{
	private static readonly Guid OwnOrganization = Guid.Parse("11111111-1111-1111-1111-111111111111");
	private static readonly Guid OtherOrganization = Guid.Parse("22222222-2222-2222-2222-222222222222");

	[Test]
	public void MembersMayReadTheirOwnOrganization()
	{
		var member = Caller(UserRole.Member, OwnOrganization);

		var act = () => OrganizationAccess.EnsureCanRead(member, OwnOrganization);

		act.Should().NotThrow();
	}

	[Test]
	public void MembersMayNotReadAnotherOrganization()
	{
		var member = Caller(UserRole.Member, OwnOrganization);

		var act = () => OrganizationAccess.EnsureCanRead(member, OtherOrganization);

		act.Should().Throw<ForbiddenAccessException>();
	}

	[Test]
	public void MembersMayNotManageTheirOwnOrganization()
	{
		var member = Caller(UserRole.Member, OwnOrganization);

		var act = () => OrganizationAccess.EnsureCanManage(member, OwnOrganization);

		act.Should().Throw<ForbiddenAccessException>();
	}

	[Test]
	public void AdministratorsMayManageTheirOwnOrganizationOnly()
	{
		var administrator = Caller(UserRole.Administrator, OwnOrganization);

		var own = () => OrganizationAccess.EnsureCanManage(administrator, OwnOrganization);
		var other = () => OrganizationAccess.EnsureCanManage(administrator, OtherOrganization);

		own.Should().NotThrow();
		other.Should().Throw<ForbiddenAccessException>();
	}

	[Test]
	public void PlatformAdministratorsMayManageEveryOrganization()
	{
		var platformAdministrator = Caller(UserRole.PlatformAdministrator, OwnOrganization);

		var act = () => OrganizationAccess.EnsureCanManage(platformAdministrator, OtherOrganization);

		act.Should().NotThrow();
	}

	[Test]
	public void OnlyPlatformAdministratorsMayManageTheirOwnRole()
	{
		var administrator = Caller(UserRole.Administrator, OwnOrganization);
		var platformAdministrator = Caller(UserRole.PlatformAdministrator, OwnOrganization);

		var escalate = () => OrganizationAccess.EnsureCanManageRole(administrator, UserRole.PlatformAdministrator);
		var ordinary = () => OrganizationAccess.EnsureCanManageRole(administrator, UserRole.Administrator);
		var allowed = () => OrganizationAccess.EnsureCanManageRole(platformAdministrator, UserRole.PlatformAdministrator);

		escalate.Should().Throw<ForbiddenAccessException>();
		ordinary.Should().NotThrow();
		allowed.Should().NotThrow();
	}

	[Test]
	public void AdministratorsMayOnlyGrantARoleBelowTheirOwn()
	{
		var administrator = Caller(UserRole.Administrator, OwnOrganization);

		var member = () => OrganizationAccess.EnsureCanGrantRole(administrator, UserRole.Member);
		var peer = () => OrganizationAccess.EnsureCanGrantRole(administrator, UserRole.Administrator);
		var escalate = () => OrganizationAccess.EnsureCanGrantRole(administrator, UserRole.PlatformAdministrator);

		member.Should().NotThrow();
		peer.Should().Throw<ForbiddenAccessException>();
		escalate.Should().Throw<ForbiddenAccessException>();
	}

	/// <summary>
	/// An administrator may still remove or rename the fellow administrator they may not have appointed, which is
	/// the whole reason granting a role is a separate question from managing somebody who holds one.
	/// </summary>
	[Test]
	public void AnAdministratorTheyMayNotGrantIsStillOneTheyMayManage()
	{
		var administrator = Caller(UserRole.Administrator, OwnOrganization);

		var grant = () => OrganizationAccess.EnsureCanGrantRole(administrator, UserRole.Administrator);
		var manage = () => OrganizationAccess.EnsureCanManageRole(administrator, UserRole.Administrator);

		grant.Should().Throw<ForbiddenAccessException>();
		manage.Should().NotThrow();
	}

	[Test]
	public void PlatformAdministratorsMayGrantAnyRole()
	{
		var platformAdministrator = Caller(UserRole.PlatformAdministrator, OwnOrganization);

		foreach (var role in Enum.GetValues<UserRole>())
		{
			var act = () => OrganizationAccess.EnsureCanGrantRole(platformAdministrator, role);

			act.Should().NotThrow();
		}
	}

	[Test]
	public void OnlyThePlatformOrganizationMayHoldAPlatformAdministrator()
	{
		var platform = new Organization { Name = "Stichting KOMPAZ", IsPlatform = true };
		var tenant = new Organization { Name = "Klant B.V." };

		var atThePlatform = () => OrganizationAccess.EnsureCanHoldRole(platform, UserRole.PlatformAdministrator);
		var atATenant = () => OrganizationAccess.EnsureCanHoldRole(tenant, UserRole.PlatformAdministrator);

		atThePlatform.Should().NotThrow();
		atATenant.Should().Throw<ConflictException>();
	}

	[Test]
	public void EveryOtherRoleIsAtHomeInEveryOrganization()
	{
		var tenant = new Organization { Name = "Klant B.V." };

		var administrator = () => OrganizationAccess.EnsureCanHoldRole(tenant, UserRole.Administrator);
		var member = () => OrganizationAccess.EnsureCanHoldRole(tenant, UserRole.Member);

		administrator.Should().NotThrow();
		member.Should().NotThrow();
	}

	[Test]
	public void ResolveTargetFallsBackToTheCallersOwnOrganization()
	{
		var member = Caller(UserRole.Member, OwnOrganization);

		OrganizationAccess.ResolveTarget(member, null).Should().Be(OwnOrganization);
		OrganizationAccess.ResolveTarget(member, OtherOrganization).Should().Be(OtherOrganization);
	}

	private static StubUser Caller(UserRole role, Guid organizationId) =>
		new(Guid.NewGuid(), organizationId, role);

	private sealed record StubUser(Guid? Id, Guid? OrganizationId, UserRole? Role) : IUser;
}
