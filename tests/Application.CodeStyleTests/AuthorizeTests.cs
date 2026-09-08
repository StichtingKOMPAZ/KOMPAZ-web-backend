using FluentAssertions;
using Kompaz.Application.Common.Security;
using Kompaz.Application.Users.Commands.InviteUser;
using MediatR;
using NUnit.Framework;
using System.Reflection;

namespace Kompaz.Application.CodeStyleTests;

/// <summary>
/// Who may execute a use case is decided on the request, so it holds for every caller rather than for whichever
/// endpoint happened to be wired up. This asserts the decision was made at all: a request carrying neither
/// attribute is refused at runtime, and that is a poor place to learn about it.
/// </summary>
[TestFixture]
internal class AuthorizeTests
{
	private static readonly Type[] RequestTypes = typeof(InviteUserCommand).Assembly
		.GetTypes()
		.Where(type => type is { IsAbstract: false, IsInterface: false })
		.Where(type => type.GetInterfaces().Any(@interface =>
			@interface == typeof(IRequest) ||
			(@interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IRequest<>))))
		.ToArray();

	[Test]
	public void EveryRequestShouldStateWhoMayExecuteIt()
	{
		RequestTypes.Should().NotBeEmpty();

		string[] undecided = RequestTypes
			.Where(type => !HasAttribute<AuthorizeAttribute>(type) && !HasAttribute<AllowAnonymousAttribute>(type))
			.Select(type => type.Name)
			.ToArray();

		undecided.Should().BeEmpty(
			"every request needs [Authorize] or [AllowAnonymous]; these have neither: {0}",
			string.Join(", ", undecided));
	}

	/// <summary>
	/// Both at once is a contradiction, and the behaviour would silently honour only one of them.
	/// </summary>
	[Test]
	public void NoRequestShouldClaimToBeBothAuthorizedAndAnonymous()
	{
		string[] contradictory = RequestTypes
			.Where(type => HasAttribute<AuthorizeAttribute>(type) && HasAttribute<AllowAnonymousAttribute>(type))
			.Select(type => type.Name)
			.ToArray();

		contradictory.Should().BeEmpty(
			"a request cannot be both; these claim both: {0}",
			string.Join(", ", contradictory));
	}

	/// <summary>
	/// Anonymous is the exception, not a habit. Anything new here is a decision worth a second look, and the list
	/// names what is deliberately open so that growth in it is visible in a diff.
	/// </summary>
	[Test]
	public void OnlyTheAuthenticationEntryPointsShouldBeAnonymous()
	{
		string[] expected =
		[
			"RedeemLoginTokenCommand",
			"RefreshAccessTokenCommand",
			"RequestMagicLinkCommand",
			"RevokeRefreshTokenCommand",
		];

		var anonymous = RequestTypes
			.Where(HasAttribute<AllowAnonymousAttribute>)
			.Select(type => type.Name)
			.OrderBy(name => name, StringComparer.Ordinal);

		anonymous.Should().Equal(expected);
	}

	private static bool HasAttribute<TAttribute>(Type type)
		where TAttribute : Attribute =>
		type.GetCustomAttributes<TAttribute>().Any();
}
