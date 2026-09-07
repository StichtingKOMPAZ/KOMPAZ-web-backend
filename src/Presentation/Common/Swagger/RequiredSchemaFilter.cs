using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Kompaz.Presentation.Common.Swagger;

/// <summary>
/// Marks every non-nullable property as required so the generated schema reflects nullable reference type intent.
/// </summary>
internal sealed class RequiredSchemaFilter : ISchemaFilter
{
	public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
	{
		if (schema is not OpenApiSchema concrete || concrete.Properties is not { Count: > 0 })
		{
			return;
		}

		concrete.Required ??= new HashSet<string>(StringComparer.Ordinal);

		foreach (var (name, property) in concrete.Properties)
		{
			if (property.Type is { } type && type.HasFlag(JsonSchemaType.Null))
			{
				continue;
			}

			concrete.Required.Add(name);
		}
	}
}
