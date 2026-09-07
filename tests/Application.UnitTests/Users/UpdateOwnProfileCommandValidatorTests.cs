using FluentAssertions;
using Kompaz.Application.Users.Commands.UpdateOwnProfile;
using NUnit.Framework;

namespace Kompaz.Application.UnitTests.Users;

[TestFixture]
internal class UpdateOwnProfileCommandValidatorTests
{
	private UpdateOwnProfileCommandValidator _validator = null!;

	[SetUp]
	public void SetUp()
	{
		_validator = new UpdateOwnProfileCommandValidator();
	}

	[Test]
	public async Task ShouldAcceptANewName()
	{
		var result = await _validator.ValidateAsync(new UpdateOwnProfileCommand("Sanne Jansen"));

		result.IsValid.Should().BeTrue();
	}

	[TestCase("")]
	[TestCase("   ")]
	public async Task ShouldRejectAnEmptyName(string name)
	{
		var result = await _validator.ValidateAsync(new UpdateOwnProfileCommand(name));

		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateOwnProfileCommand.Name));
	}
}
