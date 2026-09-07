using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kompaz.Infrastructure.Persistence;

/// <summary>
/// Brings the database up to date and plants the first platform administrator, without which nobody could sign in
/// to invite anyone else.
/// </summary>
public class ApplicationDbContextInitialiser
{
	private const string PlatformOrganizationName = "KOMPAZ";
	private const string PlatformAdministratorEmail = "admin@kompaz.local";

	private readonly ApplicationDbContext _context;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<ApplicationDbContextInitialiser> _logger;

	public ApplicationDbContextInitialiser(
		ApplicationDbContext context,
		TimeProvider timeProvider,
		ILogger<ApplicationDbContextInitialiser> logger)
	{
		_context = context;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public async Task InitialiseAsync(CancellationToken cancellationToken = default)
	{
		await _context.Database.MigrateAsync(cancellationToken);
	}

	public async Task SeedAsync(CancellationToken cancellationToken = default)
	{
		if (await _context.Organizations.AnyAsync(cancellationToken))
		{
			return;
		}

		var now = _timeProvider.GetUtcNow();

		var organization = new Organization
		{
			Name = PlatformOrganizationName,
			CreatedUtc = now,
			UpdatedUtc = now,
		};

		_context.Organizations.Add(organization);

		var administrator = User.Invite(
			organization.Id,
			PlatformAdministratorEmail,
			"Platform Administrator",
			UserRole.PlatformAdministrator,
			now);

		administrator.Activate(now);
		_context.Users.Add(administrator);

		await _context.SaveChangesAsync(cancellationToken);

		_logger.LogInformation(
			"Seeded organization {OrganizationName} with platform administrator {Email}. Request a sign-in link for that address to get started.",
			PlatformOrganizationName,
			PlatformAdministratorEmail);
	}
}
