using FluentAssertions;
using Kompaz.Application.Common.Behaviours;
using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Enums;
using MediatR;
using NUnit.Framework;

namespace Kompaz.Application.UnitTests.Common;

/// <summary>
/// The behaviour is the door every use case goes through, and a request that never says who may execute it has to
/// find that door shut. A code-style test catches the omission earlier, but only for types it can see, so this pins
/// what happens when one gets past it.
/// </summary>
[TestFixture]
internal class AuthorizationBehaviourTests
{
	private static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");
	private static readonly Guid Organization = Guid.Parse("22222222-2222-2222-2222-222222222222");

	[Test]
	public async Task ARequestThatSaysNothingIsRefused()
	{
		var act = () => HandleAsync(new UndecidedRequest(), SignedIn(UserRole.PlatformAdministrator));

		(await act.Should().ThrowAsync<ForbiddenAccessException>())
			.Which.Message.Should().Contain(nameof(UndecidedRequest));
	}

	/// <summary>
	/// Refused even for a platform administrator: the point is that nobody decided, not that the caller is too junior.
	/// </summary>
	[Test]
	public async Task ARequestThatSaysNothingIsRefusedEvenForTheHighestRole()
	{
		var act = () => HandleAsync(new UndecidedRequest(), SignedIn(UserRole.PlatformAdministrator));

		await act.Should().ThrowAsync<ForbiddenAccessException>();
	}

	[Test]
	public async Task AnAnonymousRequestIsLetThrough()
	{
		bool reached = await HandleAsync(new OpenRequest(), Anonymous());

		reached.Should().BeTrue();
	}

	[Test]
	public async Task AnAuthorizedRequestNeedsACaller()
	{
		var act = () => HandleAsync(new MemberRequest(), Anonymous());

		await act.Should().ThrowAsync<UnauthorizedAccessException>();
	}

	[Test]
	public async Task AnAuthorizedRequestNeedsTheRole()
	{
		var act = () => HandleAsync(new AdministratorRequest(), SignedIn(UserRole.Member));

		await act.Should().ThrowAsync<ForbiddenAccessException>();
	}

	[Test]
	public async Task AHigherRoleSatisfiesALowerRequirement()
	{
		bool reached = await HandleAsync(new MemberRequest(), SignedIn(UserRole.PlatformAdministrator));

		reached.Should().BeTrue();
	}

	private static async Task<bool> HandleAsync<TRequest>(TRequest request, IUser user)
		where TRequest : notnull
	{
		var behaviour = new AuthorizationBehaviour<TRequest, bool>(user);

		return await behaviour.Handle(request, () => Task.FromResult(true), CancellationToken.None);
	}

	private static StubUser Anonymous() => new(null, null, null);

	private static StubUser SignedIn(UserRole role) => new(Caller, Organization, role);

	private sealed record StubUser(Guid? Id, Guid? OrganizationId, UserRole? Role) : IUser;

	private sealed record UndecidedRequest : IRequest<bool>;

	[AllowAnonymous]
	private sealed record OpenRequest : IRequest<bool>;

	[Authorize]
	private sealed record MemberRequest : IRequest<bool>;

	[Authorize(MinimumRole = UserRole.Administrator)]
	private sealed record AdministratorRequest : IRequest<bool>;
}
