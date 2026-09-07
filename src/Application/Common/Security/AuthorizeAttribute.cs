using Kompaz.Domain.Enums;

namespace Kompaz.Application.Common.Security;

/// <summary>
/// Marks a request as requiring an authenticated caller holding at least <see cref="MinimumRole"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AuthorizeAttribute : Attribute
{
	/// <summary>
	/// Gets the lowest role permitted to execute the request. Roles are hierarchical, so a higher role also passes.
	/// </summary>
	public UserRole MinimumRole { get; init; } = UserRole.Member;
}
