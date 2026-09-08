using Kompaz.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using System.Globalization;

namespace Kompaz.Application.FunctionalTests;

/// <summary>
/// Boots the API against a database of its own on the shared PostgreSQL server, plus a capturing email transport, so
/// every test starts from the same seeded state and can read the sign-in links the application "sent".
/// <para>
/// Only the outside world is replaced — email, and the clock. The database is the real provider against a real
/// server, reached by handing the host a connection string, so persistence behaves in a test the way it will in
/// production rather than the way a substitute would.
/// </para>
/// </summary>
internal sealed class CustomWebApplicationFactory : WebApplicationFactory<Kompaz.Presentation.Program>
{
	/// <summary>
	/// The idle window a refresh token gets, restarted on every exchange.
	/// </summary>
	public const int SlidingLifetimeDays = 14;

	/// <summary>
	/// The ceiling a refresh-token session reaches however often it is refreshed.
	/// </summary>
	public const int AbsoluteLifetimeDays = 90;

	private readonly IReadOnlyDictionary<string, string> _settings;

	public CustomWebApplicationFactory()
		: this(new Dictionary<string, string>(StringComparer.Ordinal))
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="CustomWebApplicationFactory"/> class with extra configuration on
	/// top of the defaults below, for a test about how the application is wired rather than about what an endpoint
	/// returns.
	/// </summary>
	/// <param name="settings">Configuration to state on the host, overriding the defaults.</param>
	public CustomWebApplicationFactory(IReadOnlyDictionary<string, string> settings)
	{
		_settings = settings;
	}

	public CapturingEmailSender Emails { get; } = new();

	/// <summary>
	/// The clock the application runs on. Tests advance it to reach behaviour that is otherwise days away, such as a
	/// refresh token sliding forward or a session hitting its absolute ceiling.
	/// </summary>
	public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// The host runs from the API project's content root, so its appsettings files are the ones in play. Anything
		// the tests need to differ is stated here rather than in a file that would silently not be read.
		//
		// These go through UseSetting, not ConfigureAppConfiguration: service registration reads configuration while
		// the builder is being assembled, which is before ConfigureAppConfiguration callbacks run.
		builder.UseSetting("Swagger", "false");

		// An empty database, which the application then migrates and seeds itself.
		builder.UseSetting("ConnectionStrings:KompazDb", PostgresFixture.CreateDatabase());

		// Tests drive the sign-in and refresh endpoints far harder than a real client would.
		builder.UseSetting("RateLimiting:PermitLimit", "100000");
		builder.UseSetting("RateLimiting:SignInPermitLimit", "100000");

		// Stated rather than inherited, because the refresh-token tests do arithmetic against these numbers.
		builder.UseSetting("Authentication:AccessTokenLifetimeMinutes", "60");
		builder.UseSetting("Authentication:MagicLinkLifetimeMinutes", "15");
		builder.UseSetting("Authentication:InvitationLifetimeDays", "7");
		builder.UseSetting("Authentication:RefreshTokenSlidingLifetimeDays", SlidingLifetimeDays.ToString(CultureInfo.InvariantCulture));
		builder.UseSetting("Authentication:RefreshTokenAbsoluteLifetimeDays", AbsoluteLifetimeDays.ToString(CultureInfo.InvariantCulture));

		// Last, so a test that states one of these on purpose wins over the defaults above.
		foreach (var (key, value) in _settings)
		{
			builder.UseSetting(key, value);
		}

		builder.ConfigureServices(services =>
		{
			services.RemoveAll<IAuthenticationEmailSender>();
			services.AddSingleton<IAuthenticationEmailSender>(Emails);

			services.RemoveAll<TimeProvider>();
			services.AddSingleton<TimeProvider>(Clock);
		});
	}
}
