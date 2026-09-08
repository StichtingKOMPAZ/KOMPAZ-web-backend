using FluentAssertions;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Enums;
using Kompaz.Presentation.Infrastructure;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using System.Security.Claims;

namespace Kompaz.Application.FunctionalTests.Infrastructure;

/// <summary>
/// The caller is read from claims, and every authorization decision is made against what comes back. A role that is
/// not one of ours has to read as no role at all rather than as something that outranks the ones that are.
/// </summary>
[TestFixture]
internal sealed class CurrentUserTests
{
	[Test]
	public void ANamedRoleIsRead()
	{
		CurrentUserWithRole(nameof(UserRole.Administrator)).Role.Should().Be(UserRole.Administrator);
	}

	[Test]
	public void ANumericRoleIsRefused()
	{
		// Enum.TryParse accepts "99" and would hand back (UserRole)99, which is greater than every role there is.
		CurrentUserWithRole("99").Role.Should().BeNull();
	}

	[Test]
	public void TheNumberOfARealRoleIsRefusedToo()
	{
		CurrentUserWithRole("2").Role.Should().BeNull();
	}

	[Test]
	public void AWronglyCasedRoleIsRefused()
	{
		CurrentUserWithRole("administrator").Role.Should().BeNull();
	}

	[Test]
	public void AMissingRoleIsNoRole()
	{
		new CurrentUser(new HttpContextAccessor()).Role.Should().BeNull();
	}

	private static CurrentUser CurrentUserWithRole(string role)
	{
		var identity = new ClaimsIdentity([new Claim(KompazClaimTypes.Role, role)], "Test");

		var accessor = new HttpContextAccessor
		{
			HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
		};

		return new CurrentUser(accessor);
	}
}
