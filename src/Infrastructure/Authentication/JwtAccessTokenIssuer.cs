using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Models;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Kompaz.Infrastructure.Authentication;

/// <summary>
/// Issues signed JSON Web Tokens carrying the identity, tenant, and role a request is authorized against.
/// </summary>
internal sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
	private readonly AuthenticationSettings _settings;
	private readonly TimeProvider _timeProvider;
	private readonly SigningCredentials _signingCredentials;

	public JwtAccessTokenIssuer(IOptions<AuthenticationSettings> settings, TimeProvider timeProvider)
	{
		_settings = settings.Value;
		_timeProvider = timeProvider;
		_signingCredentials = new SigningCredentials(
			new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey)),
			SecurityAlgorithms.HmacSha256);
	}

	public AccessToken Issue(User user)
	{
		var issuedAt = _timeProvider.GetUtcNow();
		var expiresUtc = issuedAt.Add(_settings.AccessTokenLifetime);

		var descriptor = new SecurityTokenDescriptor
		{
			Issuer = _settings.Issuer,
			Audience = _settings.Audience,
			IssuedAt = issuedAt.UtcDateTime,
			NotBefore = issuedAt.UtcDateTime,
			Expires = expiresUtc.UtcDateTime,
			SigningCredentials = _signingCredentials,
			Claims = new Dictionary<string, object>(StringComparer.Ordinal)
			{
				[KompazClaimTypes.Subject] = user.Id.ToString(),
				[KompazClaimTypes.Email] = user.Email,
				[KompazClaimTypes.Name] = user.Name,
				[KompazClaimTypes.Organization] = user.OrganizationId.ToString(),
				[KompazClaimTypes.Role] = user.Role.ToString(),
				[JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
			},
		};

		string value = new JsonWebTokenHandler().CreateToken(descriptor);

		return new AccessToken(value, expiresUtc);
	}
}
