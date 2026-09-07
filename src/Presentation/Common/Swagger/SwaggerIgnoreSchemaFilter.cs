using Kompaz.Application.Common.Swagger;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

namespace Kompaz.Presentation.Common.Swagger;

/// <summary>
/// Removes properties and fields annotated with <see cref="SwaggerIgnoreAttribute"/> from generated model schemas.
/// </summary>
internal sealed class SwaggerIgnoreSchemaFilter : ISchemaFilter
{
	public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
	{
		if (schema.Properties is not { Count: > 0 } properties)
		{
			return;
		}

		var ignoredNames = context.Type
			.GetMembers(BindingFlags.Public | BindingFlags.Instance)
			.Where(member => member is PropertyInfo or FieldInfo
				&& member.GetCustomAttribute<SwaggerIgnoreAttribute>() is not null)
			.SelectMany(member => new[] { member.Name, ToCamelCase(member.Name) });

		foreach (string name in ignoredNames)
		{
			properties.Remove(name);
		}
	}

	private static string ToCamelCase(string value) =>
		value.Length > 0 && char.IsUpper(value[0])
			? char.ToLowerInvariant(value[0]) + value[1..]
			: value;
}
