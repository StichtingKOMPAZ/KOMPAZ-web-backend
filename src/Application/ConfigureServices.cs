using Kompaz.Application.Authentication;
using Kompaz.Application.Common.Behaviours;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using System.Reflection;

namespace Kompaz.Application;

public static class ConfigureServices
{
	public static IServiceCollection AddApplicationServices(this IServiceCollection services)
	{
		// The stock wording of every rule, in Dutch. Set once here rather than a WithMessage on each rule:
		// FluentValidation ships the translations, and overriding them one by one is how half of an API ends
		// up answering in a different language from the other half. A rule whose wording the product dictates
		// still states it — OrganizationMessages holds those — and this is what everything else falls back to.
		//
		// Global static state, which is what FluentValidation offers; it is idempotent, so the several hosts a
		// test run starts do not fight over it.
		ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("nl");

		services.TryAddSingleton(TimeProvider.System);
		services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
		services.AddScoped<LoginTokenIssuer>();
		services.AddScoped<RefreshTokenIssuer>();

		services.AddMediatR(config =>
		{
			config.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
			config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehaviour<,>));
			config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
			config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehaviour<,>));

			// After authorization, which decides what the token says the caller may do, and before validation,
			// so a caller whose account is gone is turned away rather than told their request was malformed.
			config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AccountStatusBehaviour<,>));

			config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
			config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehaviour<,>));
		});

		return services;
	}
}
