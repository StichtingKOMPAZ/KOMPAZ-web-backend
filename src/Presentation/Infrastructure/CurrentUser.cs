using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Enums;
using System.Globalization;

namespace Kompaz.Presentation.Infrastructure;

/// <summary>
/// Reads the caller's identity from the validated access token on the current request.
/// </summary>
internal sealed class CurrentUser : IUser
{
	private readonly IHttpContextAccessor _httpContextAccessor;

	public CurrentUser(IHttpContextAccessor httpContextAccessor)
	{
		_httpContextAccessor = httpContextAccessor;
	}

	public Guid? Id => ReadGuid(KompazClaimTypes.Subject);

	public Guid? OrganizationId => ReadGuid(KompazClaimTypes.Organization);

	/// <summary>
	/// Gets the role named on the token, or <see langword="null"/> when it does not name one.
	/// </summary>
	public UserRole? Role => ReadRole(ReadClaim(KompazClaimTypes.Role));

	/// <summary>
	/// Accepts only the exact name of a role this application defines.
	/// <para>
	/// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> is looser than it looks. It takes numbers, so
	/// <c>"99"</c> would come back as a <c>(UserRole)99</c> that outranks every role there is, and <c>"2"</c> would
	/// come back as <see cref="UserRole.PlatformAdministrator"/> by a spelling this application never issues. The
	/// same claim also serves as ASP.NET's role claim, which only ever compares names, so accepting a second
	/// spelling here would mean the two layers could disagree about who the caller is.
	/// </para>
	/// </summary>
	private static UserRole? ReadRole(string? claim) =>
		claim is not null
		&& Enum.TryParse(claim, ignoreCase: false, out UserRole role)
		&& Enum.IsDefined(role)
		&& string.Equals(claim, role.ToString(), StringComparison.Ordinal)
			? role
			: null;

	private string? ReadClaim(string type) =>
		_httpContextAccessor.HttpContext?.User.FindFirst(type)?.Value;

	private Guid? ReadGuid(string type) =>
		Guid.TryParse(ReadClaim(type), CultureInfo.InvariantCulture, out var value) ? value : null;
}
