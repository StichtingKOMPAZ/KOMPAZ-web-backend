namespace Kompaz.Application.Common.Security;

/// <summary>
/// Marks a request as deliberately open to an unauthenticated caller.
/// <para>
/// Its purpose is to be required. A request with neither this nor <see cref="AuthorizeAttribute"/> is refused rather
/// than waved through, so forgetting to think about authorization fails instead of quietly publishing a use case.
/// Saying "anonymous" has to be a decision somebody wrote down.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllowAnonymousAttribute : Attribute;
