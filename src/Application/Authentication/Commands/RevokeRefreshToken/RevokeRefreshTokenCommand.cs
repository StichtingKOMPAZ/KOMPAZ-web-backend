using Kompaz.Application.Common.Interfaces;

namespace Kompaz.Application.Authentication.Commands.RevokeRefreshToken;

/// <summary>
/// Ends the session a refresh token belongs to. Signing out succeeds whether or not the token is still valid, so a
/// client can always clear its credentials without having to interpret an error.
/// </summary>
public record RevokeRefreshTokenCommand(string RefreshToken) : IRequest;

public class RevokeRefreshTokenCommandValidator : AbstractValidator<RevokeRefreshTokenCommand>
{
	public RevokeRefreshTokenCommandValidator()
	{
		RuleFor(command => command.RefreshToken)
			.NotEmpty()
			.MaximumLength(200);
	}
}

public class RevokeRefreshTokenCommandHandler : IRequestHandler<RevokeRefreshTokenCommand>
{
	private readonly IApplicationDbContext _context;
	private readonly ISecretTokenFactory _tokenFactory;
	private readonly TimeProvider _timeProvider;

	public RevokeRefreshTokenCommandHandler(
		IApplicationDbContext context,
		ISecretTokenFactory tokenFactory,
		TimeProvider timeProvider)
	{
		_context = context;
		_tokenFactory = tokenFactory;
		_timeProvider = timeProvider;
	}

	public async Task Handle(RevokeRefreshTokenCommand request, CancellationToken cancellationToken)
	{
		string tokenHash = _tokenFactory.Hash(request.RefreshToken);

		var stored = await _context.RefreshTokens
			.SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

		if (stored is null)
		{
			return;
		}

		var now = _timeProvider.GetUtcNow();

		var session = await _context.RefreshTokens
			.Where(token => token.SessionId == stored.SessionId && token.RevokedUtc == null)
			.ToListAsync(cancellationToken);

		foreach (var token in session)
		{
			token.Revoke(now);
		}

		await _context.SaveChangesAsync(cancellationToken);
	}
}
