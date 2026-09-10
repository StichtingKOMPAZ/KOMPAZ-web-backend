using Kompaz.Application.Authentication;
using Kompaz.Application.Common.Behaviours;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace Kompaz.Application;

public static class ConfigureServices
{
	public static IServiceCollection AddApplicationServices(this IServiceCollection services)
	{
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
