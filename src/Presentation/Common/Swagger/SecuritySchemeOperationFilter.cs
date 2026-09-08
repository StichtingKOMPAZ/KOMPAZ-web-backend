using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Kompaz.Presentation.Common.Swagger;

/// <summary>
/// Asks for the bearer token on the operations that actually need one.
/// <para>
/// Declaring it once for the whole document is easier and wrong: it would put a padlock on the sign-in endpoints,
/// which are the ones a caller reaches precisely because they have no token yet, and generated clients would treat
/// them as unreachable while unauthenticated.
/// </para>
/// </summary>
internal sealed class SecuritySchemeOperationFilter : IOperationFilter
{
	/// <summary>
	/// The name the scheme is registered under in <c>AddSecurityDefinition</c>.
	/// </summary>
	public const string SchemeName = "Bearer";

	public void Apply(OpenApiOperation operation, OperationFilterContext context)
	{
		var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

		bool requiresToken = metadata.OfType<IAuthorizeData>().Any()
			&& !metadata.OfType<IAllowAnonymous>().Any();

		if (!requiresToken)
		{
			return;
		}

		// The host document has to be handed over, or the reference has nothing to resolve against and the
		// requirement serializes as an empty object — a padlock that names no scheme.
		operation.Security =
		[
			new OpenApiSecurityRequirement
			{
				{ new OpenApiSecuritySchemeReference(SchemeName, context.Document), [] },
			},
		];
	}
}
