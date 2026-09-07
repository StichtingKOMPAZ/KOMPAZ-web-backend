using Kompaz.Application.Common.Swagger;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Kompaz.Presentation.Common.Swagger;

/// <summary>
/// Removes parameters and request-body members annotated with <see cref="SwaggerIgnoreAttribute"/> from generated operations.
/// </summary>
internal sealed class SwaggerIgnoreOperationFilter : IOperationFilter
{
	public void Apply(OpenApiOperation operation, OperationFilterContext context)
	{
		var ignoredNames = context.ApiDescription.ParameterDescriptions
			.Where(parameter => parameter.CustomAttributes().OfType<SwaggerIgnoreAttribute>().Any())
			.Select(parameter => parameter.Name)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		if (ignoredNames.Count == 0)
		{
			return;
		}

		if (operation.Parameters is { } parameters)
		{
			for (int i = parameters.Count - 1; i >= 0; i--)
			{
				if (parameters[i].Name is { } name && ignoredNames.Contains(name))
				{
					parameters.RemoveAt(i);
				}
			}
		}

		if (operation.RequestBody?.Content is null)
		{
			return;
		}

		foreach (var media in operation.RequestBody.Content.Values)
		{
			foreach (string name in ignoredNames)
			{
				media.Schema?.Properties?.Remove(name);
				media.Encoding?.Remove(name);
			}
		}
	}
}
