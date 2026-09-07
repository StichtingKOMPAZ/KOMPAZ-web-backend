using Kompaz.Application.Common.Interfaces;
using Kompaz.Infrastructure.Persistence;
using Kompaz.Presentation.Common.Cors;
using Kompaz.Presentation.Common.Errors;
using Kompaz.Presentation.Common.RateLimiting;
using Kompaz.Presentation.Common.Swagger;
using Kompaz.Presentation.Infrastructure;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.OpenApi;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

namespace Kompaz.Presentation;

internal static class ConfigureServices
{
	public static IServiceCollection AddPresentationServices(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
	{
		services.AddHealthChecks()
			.AddDbContextCheck<ApplicationDbContext>();

		services.AddHttpContextAccessor();
		services.AddScoped<IUser, CurrentUser>();

		services.ConfigureHttpJsonOptions(options =>
		{
			options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
			options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
			options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
		});

		services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsCustomization.Apply);
		services.AddExceptionHandler<CustomExceptionHandler>();
		services.AddEndpointsApiExplorer();

		var corsSettings = configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();

		services.AddCors(options =>
			options.AddDefaultPolicy(policy =>
			{
				if (corsSettings.AllowedOrigins.Length == 0)
				{
					return;
				}

				policy.WithOrigins(corsSettings.AllowedOrigins)
					.AllowAnyHeader()
					.AllowAnyMethod()
					.AllowCredentials();
			}));

		services.AddResponseCompression(options =>
		{
			options.EnableForHttps = true;
			options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json", "application/problem+json"]);
		});

		services.AddOutputCache();
		services.AddRateLimiting(configuration);
		services.AddSwagger(environment);

		return services;
	}

	private static void AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
	{
		var settings = configuration.GetSection(RateLimitSettings.SectionName).Get<RateLimitSettings>() ?? new RateLimitSettings();

		services.AddRateLimiter(options =>
		{
			options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
				RateLimitPartition.GetFixedWindowLimiter(
					ClientKey(context),
					_ => new FixedWindowRateLimiterOptions
					{
						PermitLimit = settings.PermitLimit,
						Window = TimeSpan.FromSeconds(settings.WindowSeconds),
						QueueLimit = settings.QueueLimit,
						QueueProcessingOrder = QueueProcessingOrder.OldestFirst
					}));

			options.AddPolicy(RateLimitSettings.SignInPolicyName, context =>
				RateLimitPartition.GetFixedWindowLimiter(
					ClientKey(context),
					_ => new FixedWindowRateLimiterOptions
					{
						PermitLimit = settings.SignInPermitLimit,
						Window = TimeSpan.FromSeconds(settings.SignInWindowSeconds),
						QueueLimit = 0,
						QueueProcessingOrder = QueueProcessingOrder.OldestFirst
					}));
		});
	}

	private static void AddSwagger(this IServiceCollection services, IWebHostEnvironment environment)
	{
		services.AddSwaggerGen(options =>
		{
			options.SwaggerDoc("v1", new OpenApiInfo
			{
				Version = "v1",
				Title = $"KOMPAZ API ({environment.EnvironmentName})"
			});

			options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
			{
				Description = "Enter the access token returned by POST /api/auth/tokens.",
				Name = "Authorization",
				In = ParameterLocation.Header,
				Type = SecuritySchemeType.Http,
				Scheme = "bearer",
				BearerFormat = "JWT"
			});

			options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
			{
				{ new OpenApiSecuritySchemeReference("Bearer", document), [] }
			});

			options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));
			options.SchemaFilter<RequiredSchemaFilter>();
			options.SchemaFilter<StringFormatSchemaFilter>();
			options.SchemaFilter<SwaggerIgnoreSchemaFilter>();
			options.OperationFilter<SwaggerIgnoreOperationFilter>();
		});
	}

	private static string ClientKey(HttpContext context) =>
		context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
