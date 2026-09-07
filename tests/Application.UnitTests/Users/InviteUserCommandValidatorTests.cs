using FluentAssertions;
using Kompaz.Application.Users.Commands.InviteUser;
using Kompaz.Domain.Enums;
using NUnit.Framework;

namespace Kompaz.Application.UnitTests.Users;

[TestFixture]
internal class InviteUserCommandValidatorTests
{
	private InviteUserCommandValidator _validator = null!;

	[SetUp]
	public void SetUp()
	{
		_validator = new InviteUserCommandValidator();
	}

	[Test]
	public async Task ShouldAcceptAWellFormedInvitation()
	{
		var command = new InviteUserCommand("nieuwe.collega@kompaz.local", "Nieuwe Collega", UserRole.Member);

		var result = await _validator.ValidateAsync(command);

		result.IsValid.Should().BeTrue();
	}

	[TestCase("")]
	[TestCase("not-an-email")]
	public async Task ShouldRejectAnUnusableEmailAddress(string email)
	{
		var command = new InviteUserCommand(email, "Nieuwe Collega");

		var result = await _validator.ValidateAsync(command);

		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(error => error.PropertyName == nameof(InviteUserCommand.Email));
	}

	[Test]
	public async Task ShouldRejectAnEmptyName()
	{
		var command = new InviteUserCommand("nieuwe.collega@kompaz.local", "   ");

		var result = await _validator.ValidateAsync(command);

		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(error => error.PropertyName == nameof(InviteUserCommand.Name));
	}

	[Test]
	public async Task ShouldRejectARoleOutsideTheEnum()
	{
		var command = new InviteUserCommand("nieuwe.collega@kompaz.local", "Nieuwe Collega", (UserRole)42);

		var result = await _validator.ValidateAsync(command);

		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(error => error.PropertyName == nameof(InviteUserCommand.Role));
	}
}
