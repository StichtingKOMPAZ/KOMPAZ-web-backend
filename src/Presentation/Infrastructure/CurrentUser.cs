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

	public UserRole? Role =>
		Enum.TryParse(ReadClaim(KompazClaimTypes.Role), ignoreCase: false, out UserRole role) ? role : null;

	private string? ReadClaim(string type) =>
		_httpContextAccessor.HttpContext?.User.FindFirst(type)?.Value;

	private Guid? ReadGuid(string type) =>
		Guid.TryParse(ReadClaim(type), CultureInfo.InvariantCulture, out var value) ? value : null;
}
