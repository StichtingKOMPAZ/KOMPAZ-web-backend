namespace Kompaz.Application.Common.Swagger;

/// <summary>
/// Marks a property or field so it is excluded from generated OpenAPI/Swagger schemas and operations.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class SwaggerIgnoreAttribute : Attribute;
