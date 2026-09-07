using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Kompaz.Presentation.Common.Swagger;

/// <summary>
/// Assigns OpenAPI string <c>format</c> values that the generator does not infer on its own, derived from
/// data annotations (<see cref="EmailAddressAttribute"/>, <see cref="UrlAttribute"/>) or a conventional name match.
/// </summary>
internal sealed class StringFormatSchemaFilter : ISchemaFilter
{
	public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
	{
		if (schema.Properties is not { Count: > 0 } properties)
		{
			return;
		}

		var members = context.Type
			.GetMembers(BindingFlags.Public | BindingFlags.Instance)
			.Where(member => member is PropertyInfo or FieldInfo);

		foreach (var member in members)
		{
			if (ResolveFormat(member) is not { } format)
			{
				continue;
			}

			if (TryGetStringProperty(properties, member.Name, out var target))
			{
				target.Format = format;
			}
		}
	}

	private static string? ResolveFormat(MemberInfo member)
	{
		if (member.GetCustomAttribute<EmailAddressAttribute>() is not null)
		{
			return "email";
		}

		if (member.GetCustomAttribute<UrlAttribute>() is not null)
		{
			return "uri";
		}

		if (member.Name.Equals("Email", StringComparison.OrdinalIgnoreCase)
			|| member.Name.EndsWith("Email", StringComparison.OrdinalIgnoreCase))
		{
			return "email";
		}

		return null;
	}

	private static bool TryGetStringProperty(IDictionary<string, IOpenApiSchema> properties, string memberName, out OpenApiSchema target)
	{
		target = null!;

		if (!properties.TryGetValue(memberName, out var property) && !properties.TryGetValue(ToCamelCase(memberName), out property))
		{
			return false;
		}

		if (property is OpenApiSchema concrete && concrete.Type is { } type && type.HasFlag(JsonSchemaType.String))
		{
			target = concrete;
			return true;
		}

		return false;
	}

	private static string ToCamelCase(string value) =>
		value.Length > 0 && char.IsUpper(value[0])
			? char.ToLowerInvariant(value[0]) + value[1..]
			: value;
}
