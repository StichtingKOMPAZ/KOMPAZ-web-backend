using Kompaz.Application.Common.Exceptions;
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

	/// <summary>
	/// Refuses to start against a database that is behind the code, for a deployment that applies migrations itself.
	/// Serving requests against a schema that does not match the model fails later, less clearly, and after having
	/// read or written something.
	/// </summary>
	public async Task EnsureUpToDateAsync(CancellationToken cancellationToken = default)
	{
		string[] pending = [.. await _context.Database.GetPendingMigrationsAsync(cancellationToken)];

		if (pending.Length == 0)
		{
			return;
		}

		throw new InvalidOperationException(
			$"The database is missing {pending.Length} migration(s), starting with \"{pending[0]}\". "
			+ "Database:MigrateOnStartup is off, so apply them as a deployment step — dotnet ef database update — "
			+ "before starting the application.");
	}

	public async Task SeedAsync(CancellationToken cancellationToken = default)
	{
		if (await _context.Organizations.AnyAsync(cancellationToken))
		{
			return;
		}

		var now = _timeProvider.GetUtcNow();

		var organization = new Organization { Name = PlatformOrganizationName };

		_context.Organizations.Add(organization);

		var administrator = User.Invite(
			organization.Id,
			PlatformAdministratorEmail,
			"Platform Administrator",
			UserRole.PlatformAdministrator,
			now);

		administrator.Activate(now);
		_context.Users.Add(administrator);

		try
		{
			await _context.SaveChangesAsync(cancellationToken);
		}
		catch (ConflictException)
		{
			// The check above is not a lock, so two instances starting together can both reach this point. The
			// unique index on the organization name settles it, and losing means somebody else did the work.
			_logger.LogInformation("Another instance seeded the database first.");
			return;
		}

		if (_logger.IsEnabled(LogLevel.Information))
		{
			_logger.LogInformation(
				"Seeded organization {OrganizationName} with platform administrator {Email}. Request a sign-in link for that address to get started.",
				PlatformOrganizationName,
				PlatformAdministratorEmail);
		}
	}
}
