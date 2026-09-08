using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Kompaz.Presentation.Common.Swagger;

/// <summary>
/// Documents the failures every operation can return, which the generator sees nothing of on its own: a handler's
/// return type describes success and the rest arrives as an exception. Left out, the document claims an endpoint can
/// only ever answer 201, and a generated client has no type for the 403 it will certainly meet.
/// <para>
/// Only what the endpoint's own metadata proves is added here. A conflict cannot be inferred — whether an address is
/// already taken is a fact about a handler, not about a route — so those are declared with
/// <see cref="ProducesResponseTypeAttribute"/> on the handlers that can produce one, and this filter leaves any
/// response that is already described alone.
/// </para>
/// </summary>
internal sealed class ProblemResponseOperationFilter : IOperationFilter
{
	private const string ProblemMediaType = "application/problem+json";

	public void Apply(OpenApiOperation operation, OperationFilterContext context)
	{
		var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

		bool authorized = metadata.OfType<IAuthorizeData>().Any()
			&& !metadata.OfType<IAllowAnonymous>().Any();

		bool takesInput = operation.Parameters?.Count > 0 || operation.RequestBody is not null;
		bool addressesOneThing = operation.Parameters?.Any(parameter => parameter.In == ParameterLocation.Path) ?? false;

		if (takesInput)
		{
			// Anything with something to validate can fail validation.
			Describe(operation, context, StatusCodes.Status400BadRequest, typeof(ValidationProblemDetails), "Bad Request");
		}

		if (authorized)
		{
			Describe(operation, context, StatusCodes.Status401Unauthorized, typeof(ProblemDetails), "Unauthorized");
			Describe(operation, context, StatusCodes.Status403Forbidden, typeof(ProblemDetails), "Forbidden");
		}

		if (addressesOneThing)
		{
			Describe(operation, context, StatusCodes.Status404NotFound, typeof(ProblemDetails), "Not Found");
		}

		// The rate limiter is global, so this one is true of everything.
		Describe(operation, context, StatusCodes.Status429TooManyRequests, typeof(ProblemDetails), "Too Many Requests");

		UseProblemMediaType(operation);
	}

	/// <summary>
	/// Corrects the media type on failures declared with <see cref="ProducesResponseTypeAttribute"/>, which has no
	/// way to say one. The attribute assumes <c>application/json</c>; the exception handler writes
	/// <c>application/problem+json</c>, and a client matching on content type would miss every error body.
	/// </summary>
	private static void UseProblemMediaType(OpenApiOperation operation)
	{
		if (operation.Responses is null)
		{
			return;
		}

		foreach (var response in operation.Responses)
		{
			if (!IsFailure(response.Key) || response.Value.Content is not { } content)
			{
				continue;
			}

			if (content.Remove("application/json", out var media))
			{
				content[ProblemMediaType] = media;
			}
		}
	}

	private static bool IsFailure(string statusCode) =>
		int.TryParse(statusCode, System.Globalization.CultureInfo.InvariantCulture, out int code)
		&& code >= StatusCodes.Status400BadRequest;

	private static void Describe(
		OpenApiOperation operation,
		OperationFilterContext context,
		int statusCode,
		Type problemType,
		string description)
	{
		string key = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);

		if (operation.Responses is null || operation.Responses.ContainsKey(key))
		{
			return;
		}

		operation.Responses[key] = new OpenApiResponse
		{
			Description = description,
			Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
			{
				[ProblemMediaType] = new()
				{
					Schema = context.SchemaGenerator.GenerateSchema(problemType, context.SchemaRepository),
				},
			},
		};
	}
}
