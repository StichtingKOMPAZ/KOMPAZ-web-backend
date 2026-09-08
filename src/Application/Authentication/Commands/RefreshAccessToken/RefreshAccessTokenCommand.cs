using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Application.Users;
using Kompaz.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Kompaz.Application.Authentication.Commands.RefreshAccessToken;

/// <summary>
/// Exchanges a refresh token for a new access token and a successor refresh token, sliding the session forward.
/// </summary>
/// <remarks>
/// Anonymous by necessity: the refresh token is the credential, and the access token it replaces has
/// usually expired by the time a client needs this.
/// </remarks>
[AllowAnonymous]
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
			throw await EndSessionAsReplayedAsync(stored, now, cancellationToken);
		}

		if (!stored.IsRedeemable(now))
		{
			throw new AuthenticationFailedException("The refresh token has expired.");
		}

		// The checks above cannot settle it on their own: concurrent requests carrying the same secret would both pass
		// them and both rotate. Spending the token is therefore a conditional UPDATE, and losing it is a replay like
		// any other.
		//
		// Expiry is deliberately not one of the conditions below, unlike on the login-token path. Losing this UPDATE
		// revokes the whole chain, and a token that expired in the moment between the check and the UPDATE would then
		// be punished as a replay rather than reported as expired. Expiry has no race worth closing: it only ever
		// becomes more true.
		//
		// Spending and replacing happen inside one transaction so they land together. A request that loses the race
		// revokes the whole chain, and it must not be able to do that in the gap between the two, or it would revoke
		// a chain the successor has not joined yet and leave it working.
		await using var rotation = await _context.BeginTransactionAsync(cancellationToken);

		int claimed = await _context.RefreshTokens
			.Where(token => token.TokenHash == tokenHash && token.ConsumedUtc == null && token.RevokedUtc == null)
			.ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ConsumedUtc, (DateTimeOffset?)now), cancellationToken);

		if (claimed == 0)
		{
			await rotation.RollbackAsync(cancellationToken);

			throw await EndSessionAsReplayedAsync(stored, now, cancellationToken);
		}

		var successor = _refreshTokenIssuer.Rotate(stored);
		stored.User.RecordLogin(now);

		await _context.SaveChangesAsync(cancellationToken);
		await rotation.CommitAsync(cancellationToken);

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
	/// Ends the session a replayed token belongs to and returns the failure for the caller to throw. Presenting a
	/// token that is already spent, and losing the race to spend one, are the same event: the secret is in more than
	/// one pair of hands.
	/// </summary>
	private async Task<AuthenticationFailedException> EndSessionAsReplayedAsync(
		RefreshToken stored,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		await RevokeSessionAsync(stored.SessionId, now, cancellationToken);

		_logger.LogWarning(
			"A spent refresh token was replayed for user {UserId}; session {SessionId} has been revoked.",
			stored.UserId,
			stored.SessionId);

		return new AuthenticationFailedException("The refresh token has already been used. The session has been ended.");
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
