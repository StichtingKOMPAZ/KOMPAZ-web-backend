using FluentAssertions;
using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Kompaz.Application.FunctionalTests.Persistence;

/// <summary>
/// A duplicate value has to arrive as a conflict, so that losing the race between a handler's uniqueness check and
/// its insert answers 409 rather than 500. Written against the context rather than through an endpoint, because a
/// race cannot be relied on to reach the index: the handler's own check usually gets there first.
/// <para>
/// The narrowness matters as much as the translation. Only uniqueness is a conflict — a foreign key that does not
/// exist is a bug, and turning that into a 409 would tell a client to fix something it never sent.
/// </para>
/// </summary>
[TestFixture]
internal sealed class ConflictTranslationTests : ApiTestBase
{
	[Test]
	public async Task ADuplicateEmailAddressIsAConflict()
	{
		using var scope = CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
		var organization = await context.Organizations.FirstAsync();

		// The seeder already planted this address, and the check a handler would do is skipped on purpose.
		context.Users.Add(User.Invite(
			organization.Id, SeededAdministratorEmail, "Duplicaat", UserRole.Member, Clock.GetUtcNow()));

		var save = async () => await context.SaveChangesAsync();

		await save.Should().ThrowAsync<ConflictException>();
	}

	[Test]
	public async Task ADuplicateOrganizationNameIsAConflict()
	{
		using var scope = CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
		string taken = (await context.Organizations.FirstAsync()).Name;

		var now = Clock.GetUtcNow();
		context.Organizations.Add(new Organization { Name = taken, CreatedUtc = now, UpdatedUtc = now });

		var save = async () => await context.SaveChangesAsync();

		await save.Should().ThrowAsync<ConflictException>();
	}

	[Test]
	public async Task AMissingForeignKeyIsNotAConflict()
	{
		using var scope = CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

		context.LoginTokens.Add(LoginToken.Issue(
			Guid.NewGuid(), "0123456789ABCDEF", LoginTokenPurpose.MagicLink, Clock.GetUtcNow(), TimeSpan.FromMinutes(15)));

		var save = async () => await context.SaveChangesAsync();

		(await save.Should().ThrowAsync<DbUpdateException>())
			.Which.Should().NotBeOfType<ConflictException>();
	}
}
