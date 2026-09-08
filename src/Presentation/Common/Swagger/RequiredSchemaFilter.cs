using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Kompaz.Presentation.Common.Swagger;

/// <summary>
/// Marks every non-nullable property as required so the generated schema reflects nullable reference type intent.
/// <para>
/// Depends on <c>SupportNonNullableReferenceTypes()</c> being enabled where Swagger is configured. Without it the
/// generator calls every reference type nullable however the C# reads, this filter finds nothing it is allowed to
/// mark, and it silently does nothing at all for the case it was written for.
/// </para>
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
