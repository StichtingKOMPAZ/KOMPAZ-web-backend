using Kompaz.Application.Common.Interfaces;
using Kompaz.Infrastructure.Persistence;
using Kompaz.Presentation.Common.Cors;
using Kompaz.Presentation.Common.Errors;
using Kompaz.Presentation.Common.Proxy;
using Kompaz.Presentation.Common.RateLimiting;
using Kompaz.Presentation.Common.Swagger;
using Kompaz.Presentation.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.OpenApi;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Kompaz.Presentation;

internal static class ConfigureServices
{
	public static IServiceCollection AddPresentationServices(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
	{
		services.AddHealthChecks()
			.AddDbContextCheck<ApplicationDbContext>();

		services.AddHttpContextAccessor();
		services.AddScoped<IUser, CurrentUser>();

		services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

		// The same settings again, on options nothing in this application reads at runtime. Swashbuckle builds its
		// schemas from MVC's serializer options even where there is no MVC, so leaving these two to drift means the
		// document describes a serializer nobody uses: enums were published as integers while every response carried
		// their names, and a generated client could not deserialize a single user.
		services.Configure<MvcJsonOptions>(options => ConfigureJson(options.JsonSerializerOptions));

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
		services.AddForwardedHeaders(configuration);
		services.AddRateLimiting(configuration);
		services.AddSwagger(environment);

		return services;
	}

	private static void AddForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
	{
		var section = configuration.GetSection(ForwardedHeadersSettings.SectionName);

		services.AddOptions<ForwardedHeadersSettings>()
			.Bind(section)
			.Validate(settings => settings.Validate() is null, DescribeForwardedHeadersProblem(configuration))
			.ValidateOnStart();

		var settings = section.Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();

		// A bad address here is reported by the validation above, as a configuration problem rather than as a parse
		// failure from inside a callback the host runs later.
		if (!settings.IsEnabled || settings.Validate() is not null)
		{
			return;
		}

		services.Configure<ForwardedHeadersOptions>(options =>
		{
			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
			options.ForwardLimit = settings.ForwardLimit;

			// Cleared, not added to. The defaults believe loopback, which is not what anybody asked for, and
			// leaving them in place would quietly widen the set of senders beyond the configured one.
			options.KnownProxies.Clear();
			options.KnownIPNetworks.Clear();

			foreach (string proxy in settings.KnownProxies)
			{
				options.KnownProxies.Add(IPAddress.Parse(proxy));
			}

			foreach (string network in settings.KnownNetworks)
			{
				options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
			}
		});
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

	/// <summary>
	/// How this API is serialized, in one place because two different options objects have to agree about it.
	/// </summary>
	private static void ConfigureJson(JsonSerializerOptions options)
	{
		options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
		options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
		options.Converters.Add(new JsonStringEnumConverter());
	}

	private static void AddSwagger(this IServiceCollection services, IWebHostEnvironment environment)
	{
		services.AddSwaggerGen(options =>
		{
			// Without this every reference type is published as nullable and optional, however the C# reads —
			// so a required string looked optional, and RequiredSchemaFilter below could never see one to mark.
			options.SupportNonNullableReferenceTypes();

			options.SwaggerDoc("v1", new OpenApiInfo
			{
				Version = "v1",
				Title = $"KOMPAZ API ({environment.EnvironmentName})"
			});

			options.AddSecurityDefinition(SecuritySchemeOperationFilter.SchemeName, new OpenApiSecurityScheme
			{
				Description = "Enter the access token returned by POST /api/auth/tokens.",
				Name = "Authorization",
				In = ParameterLocation.Header,
				Type = SecuritySchemeType.Http,
				Scheme = "bearer",
				BearerFormat = "JWT"
			});

			options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));
			options.SchemaFilter<RequiredSchemaFilter>();
			options.SchemaFilter<StringFormatSchemaFilter>();
			options.SchemaFilter<SwaggerIgnoreSchemaFilter>();
			options.OperationFilter<SwaggerIgnoreOperationFilter>();
			options.OperationFilter<SecuritySchemeOperationFilter>();
			options.OperationFilter<ProblemResponseOperationFilter>();
		});
	}

	private static string DescribeForwardedHeadersProblem(IConfiguration configuration) =>
		(configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>()
			?? new ForwardedHeadersSettings()).Validate()
			?? "Forwarded header configuration is invalid.";

	/// <summary>
	/// The partition a caller is rate limited within. Behind a proxy this is only the caller once the forwarded
	/// headers have been believed, which is what the "ForwardedHeaders" section is for.
	/// </summary>
	private static string ClientKey(HttpContext context) =>
		context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
