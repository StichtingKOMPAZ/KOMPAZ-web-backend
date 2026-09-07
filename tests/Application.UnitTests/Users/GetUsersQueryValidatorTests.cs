using FluentAssertions;
using Kompaz.Application.Common.Models;
using Kompaz.Application.Users.Queries.GetUsers;
using Kompaz.Domain.Enums;
using NUnit.Framework;

namespace Kompaz.Application.UnitTests.Users;

[TestFixture]
internal class GetUsersQueryValidatorTests
{
	private GetUsersQueryValidator _validator = null!;

	[SetUp]
	public void SetUp()
	{
		_validator = new GetUsersQueryValidator();
	}

	[Test]
	public async Task ShouldAcceptTheDefaultPage()
	{
		var result = await _validator.ValidateAsync(new GetUsersQuery { Status = UserStatus.Invited });

		result.IsValid.Should().BeTrue();
	}

	[TestCase(0)]
	[TestCase(-1)]
	public async Task ShouldRejectAPageNumberBelowOne(int pageNumber)
	{
		var result = await _validator.ValidateAsync(new GetUsersQuery { PageNumber = pageNumber });

		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(error => error.PropertyName == nameof(GetUsersQuery.PageNumber));
	}

	[Test]
	public async Task ShouldRejectAPageLargerThanTheCap()
	{
		var result = await _validator.ValidateAsync(new GetUsersQuery { PageSize = PagedQuery.MaximumPageSize + 1 });

		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(error => error.PropertyName == nameof(GetUsersQuery.PageSize));
	}
}
