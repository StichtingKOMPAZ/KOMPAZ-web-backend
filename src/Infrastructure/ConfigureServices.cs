using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Infrastructure.Authentication;
using Kompaz.Infrastructure.Email;
using Kompaz.Infrastructure.Persistence;
using Kompaz.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Kompaz.Infrastructure;

public static class ConfigureServices
{
	/// <summary>
	/// Whether an instance migrates the database as it starts. True is right for one instance and for a developer
	/// machine. Several instances starting together would race each other, so a deployment that scales out turns
	/// this off and applies migrations as a step of its own; startup then refuses to serve a database that is behind.
	/// </summary>
	private const string MigrateOnStartupKey = "Database:MigrateOnStartup";

	public static IServiceCollection AddInfrastructureServices(
		this IServiceCollection services,
		IConfiguration configuration,
		IHostEnvironment environment)
	{
		services.AddScoped<AuditableEntityInterceptor>();
		services.AddScoped<DispatchDomainEventsInterceptor>();

		services.AddDbContext<ApplicationDbContext>((provider, options) =>
			options
				.UseNpgsql(configuration.GetConnectionString("KompazDb"))
				.AddInterceptors(
					provider.GetRequiredService<AuditableEntityInterceptor>(),
					provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

		services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
		services.AddScoped<ApplicationDbContextInitialiser>();

		services.AddAuthenticationServices(configuration);
		services.AddEmailServices(configuration, environment);

		return services;
	}

	/// <summary>
	/// Brings the database up to date, unless a deployment would rather do that itself.
	/// </summary>
	public static async Task<WebApplication> InitialiseAndSeedDatabaseAsync(this WebApplication app)
	{
		using var scope = app.Services.CreateScope();

		// Configuration first. Every ValidateOnStart check otherwise runs inside RunAsync, which is after this
		// method has already created, migrated and seeded a database for a deployment that was going to fail anyway.
		scope.ServiceProvider.GetRequiredService<IStartupValidator>().Validate();

		var initializer = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

		if (app.Configuration.GetValue(MigrateOnStartupKey, true))
		{
			await initializer.InitialiseAsync();
		}
		else
		{
			await initializer.EnsureUpToDateAsync();
		}

		await initializer.SeedAsync();

		return app;
	}

	private static void AddAuthenticationServices(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddOptions<AuthenticationSettings>()
			.Bind(configuration.GetSection(AuthenticationSettings.SectionName))
			.Validate(settings => settings.Validate() is null, DescribeAuthenticationProblem(configuration))
			.ValidateOnStart();

		services.TryAddSingleton(TimeProvider.System);
		services.AddScoped<IAuthenticationSettings>(provider =>
			provider.GetRequiredService<IOptions<AuthenticationSettings>>().Value);
		services.AddSingleton<ISecretTokenFactory, SecretTokenFactory>();
		services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

		var settings = configuration.GetSection(AuthenticationSettings.SectionName).Get<AuthenticationSettings>() ?? new AuthenticationSettings();

		services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
			.AddJwtBearer(options =>
			{
				options.MapInboundClaims = false;
				options.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuer = true,
					ValidIssuer = settings.Issuer,
					ValidateAudience = true,
					ValidAudience = settings.Audience,
					ValidateIssuerSigningKey = true,
					IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PadKey(settings.SigningKey))),
					ValidateLifetime = true,
					ClockSkew = TimeSpan.FromSeconds(30),
					NameClaimType = KompazClaimTypes.Name,
					RoleClaimType = KompazClaimTypes.Role,
				};
			});

		// The library reads the system clock directly, which would leave token validation on a different clock from
		// the one every use case runs on. Validating lifetimes against the injected TimeProvider keeps them in step.
		services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
			.Configure<TimeProvider>((options, timeProvider) =>
				options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, parameters) =>
					IsWithinLifetime(notBefore, expires, parameters.ClockSkew, timeProvider.GetUtcNow().UtcDateTime));

		services.AddAuthorizationBuilder();
	}

	private static void AddEmailServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
	{
		var section = configuration.GetSection(EmailSettings.SectionName);

		services.AddOptions<EmailSettings>()
			.Bind(section)
			.Validate(settings => settings.Validate() is null, DescribeEmailProblem(configuration))
			.ValidateOnStart();

		var settings = section.Get<EmailSettings>() ?? new EmailSettings();

		if (settings.Smtp.IsConfigured)
		{
			services.AddScoped<IEmailDispatcher, SmtpEmailDispatcher>();
		}
		else if (environment.IsDevelopment())
		{
			services.AddScoped<IEmailDispatcher, LoggingEmailDispatcher>();
		}
		else
		{
			// The fallback sink writes sign-in links to the log, which is only acceptable on a developer machine.
			// Outside development a relay is mandatory: refuse to start rather than quietly log everybody's
			// credentials, the same way a missing signing key refuses to start rather than sign with a shared secret.
			throw new InvalidOperationException(
				$"{EmailSettings.SectionName}:{nameof(EmailSettings.Smtp)} must be configured with Host, UserName, and "
				+ $"Password outside the Development environment (current environment: {environment.EnvironmentName}). "
				+ "Without a relay, sign-in links would be written to the application log.");
		}

		services.AddScoped<IAuthenticationEmailSender, AuthenticationEmailSender>();
	}

	private static bool IsWithinLifetime(DateTime? notBefore, DateTime? expires, TimeSpan clockSkew, DateTime now) =>
		(notBefore is null || notBefore <= now.Add(clockSkew))
		&& (expires is null || expires > now.Subtract(clockSkew));

	private static string DescribeAuthenticationProblem(IConfiguration configuration) =>
		(configuration.GetSection(AuthenticationSettings.SectionName).Get<AuthenticationSettings>() ?? new AuthenticationSettings()).Validate()
			?? "Authentication configuration is invalid.";

	private static string DescribeEmailProblem(IConfiguration configuration) =>
		(configuration.GetSection(EmailSettings.SectionName).Get<EmailSettings>() ?? new EmailSettings()).Validate()
			?? "Email configuration is invalid.";

	/// <summary>
	/// Keeps token validation constructible when the signing key is missing so that startup fails with the
	/// configuration message from <see cref="AuthenticationSettings.Validate"/> rather than a cryptography error.
	/// </summary>
	private static string PadKey(string signingKey) =>
		Encoding.UTF8.GetByteCount(signingKey) >= 32 ? signingKey : new string('0', 32);
}
