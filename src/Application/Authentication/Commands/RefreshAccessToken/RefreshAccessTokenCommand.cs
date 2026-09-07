using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Users;
using Microsoft.Extensions.Logging;

namespace Kompaz.Application.Authentication.Commands.RefreshAccessToken;

/// <summary>
/// Exchanges a refresh token for a new access token and a successor refresh token, sliding the session forward.
/// </summary>
public record RefreshAccessTokenCommand(string RefreshToken) : IRequest<AuthenticationResultDto>;

public class RefreshAccessTokenCommandValidator : AbstractValidator<RefreshAccessTokenCommand>
{
	public RefreshAccessTokenCommandValidator()
	{
		RuleFor(command => command.RefreshToken)
			.NotEmpty()
			.MaximumLength(200);
	}
}

public class RefreshAccessTokenCommandHandler : IRequestHandler<RefreshAccessTokenCommand, AuthenticationResultDto>
{
	private readonly IApplicationDbContext _context;
	private readonly ISecretTokenFactory _tokenFactory;
	private readonly IAccessTokenIssuer _accessTokenIssuer;
	private readonly RefreshTokenIssuer _refreshTokenIssuer;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<RefreshAccessTokenCommandHandler> _logger;

	public RefreshAccessTokenCommandHandler(
		IApplicationDbContext context,
		ISecretTokenFactory tokenFactory,
		IAccessTokenIssuer accessTokenIssuer,
		RefreshTokenIssuer refreshTokenIssuer,
		TimeProvider timeProvider,
		ILogger<RefreshAccessTokenCommandHandler> logger)
	{
		_context = context;
		_tokenFactory = tokenFactory;
		_accessTokenIssuer = accessTokenIssuer;
		_refreshTokenIssuer = refreshTokenIssuer;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public async Task<AuthenticationResultDto> Handle(RefreshAccessTokenCommand request, CancellationToken cancellationToken)
	{
		string tokenHash = _tokenFactory.Hash(request.RefreshToken);
		var now = _timeProvider.GetUtcNow();

		var stored = await _context.RefreshTokens
			.Include(token => token.User)
				.ThenInclude(user => user.Organization)
			.SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken)
			?? throw new AuthenticationFailedException("The refresh token is not valid.");

		if (stored.IsSpent)
		{
			await RevokeSessionAsync(stored.SessionId, now, cancellationToken);

			_logger.LogWarning(
				"A spent refresh token was replayed for user {UserId}; session {SessionId} has been revoked.",
				stored.UserId,
				stored.SessionId);

			throw new AuthenticationFailedException("The refresh token has already been used. The session has been ended.");
		}

		if (!stored.IsRedeemable(now))
		{
			throw new AuthenticationFailedException("The refresh token has expired.");
		}

		var successor = _refreshTokenIssuer.Rotate(stored);
		stored.User.RecordLogin(now);

		await _context.SaveChangesAsync(cancellationToken);

		var accessToken = _accessTokenIssuer.Issue(stored.User);

		return new AuthenticationResultDto(
			accessToken.Value,
			"Bearer",
			accessToken.ExpiresUtc,
			successor.Value,
			successor.ExpiresUtc,
			UserDto.FromEntity(stored.User));
	}

	/// <summary>
	/// Withdraws every token in a chain. A replay means the secret is loose, so the whole session goes, not just the
	/// token that was presented.
	/// </summary>
	private async Task RevokeSessionAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken)
	{
		var session = await _context.RefreshTokens
			.Where(token => token.SessionId == sessionId && token.RevokedUtc == null)
			.ToListAsync(cancellationToken);

		foreach (var token in session)
		{
			token.Revoke(now);
		}

		await _context.SaveChangesAsync(cancellationToken);
	}
}
