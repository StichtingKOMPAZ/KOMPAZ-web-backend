using FluentAssertions;
using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
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
