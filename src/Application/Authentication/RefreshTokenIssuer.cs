using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Models;
using Kompaz.Domain.Entities;

namespace Kompaz.Application.Authentication;

/// <summary>
/// Starts and continues refresh-token sessions. The caller persists the change, so a rotation and whatever it was
/// issued alongside commit together.
/// </summary>
public sealed class RefreshTokenIssuer
{
	private readonly IApplicationDbContext _context;
	private readonly ISecretTokenFactory _tokenFactory;
	private readonly IAuthenticationSettings _settings;
	private readonly TimeProvider _timeProvider;

	public RefreshTokenIssuer(
		IApplicationDbContext context,
		ISecretTokenFactory tokenFactory,
		IAuthenticationSettings settings,
		TimeProvider timeProvider)
	{
		_context = context;
		_tokenFactory = tokenFactory;
		_settings = settings;
		_timeProvider = timeProvider;
	}

	/// <summary>
	/// Opens a new session for a user who has just proven their identity. Sessions already open elsewhere are left
	/// alone, so signing in on one device does not sign the user out on another.
	/// </summary>
	public RefreshTokenGrant StartSession(User user)
	{
		var secret = _tokenFactory.Create();
		var token = RefreshToken.StartSession(
			user.Id,
			secret.Hash,
			_timeProvider.GetUtcNow(),
			_settings.RefreshTokenSlidingLifetime,
			_settings.RefreshTokenAbsoluteLifetime);

		_context.RefreshTokens.Add(token);

		return new RefreshTokenGrant(secret.Value, token.ExpiresUtc);
	}

	/// <summary>
	/// Spends <paramref name="current"/> and opens its successor, restarting the sliding window.
	/// </summary>
	public RefreshTokenGrant Rotate(RefreshToken current)
	{
		var secret = _tokenFactory.Create();
		var successor = current.Rotate(secret.Hash, _timeProvider.GetUtcNow(), _settings.RefreshTokenSlidingLifetime);

		_context.RefreshTokens.Add(successor);

		return new RefreshTokenGrant(secret.Value, successor.ExpiresUtc);
	}
}
