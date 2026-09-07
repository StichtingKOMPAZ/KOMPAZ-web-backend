using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Users;

namespace Kompaz.Application.Authentication.Commands.RedeemLoginToken;

/// <summary>
/// Exchanges the secret from a sign-in link for an access token. Redeeming a link also activates an invited user.
/// </summary>
public record RedeemLoginTokenCommand(string Token) : IRequest<AuthenticationResultDto>;

public class RedeemLoginTokenCommandValidator : AbstractValidator<RedeemLoginTokenCommand>
{
	public RedeemLoginTokenCommandValidator()
	{
		RuleFor(command => command.Token)
			.NotEmpty()
			.MaximumLength(200);
	}
}

public class RedeemLoginTokenCommandHandler : IRequestHandler<RedeemLoginTokenCommand, AuthenticationResultDto>
{
	private readonly IApplicationDbContext _context;
	private readonly ISecretTokenFactory _tokenFactory;
	private readonly IAccessTokenIssuer _accessTokenIssuer;
	private readonly RefreshTokenIssuer _refreshTokenIssuer;
	private readonly TimeProvider _timeProvider;

	public RedeemLoginTokenCommandHandler(
		IApplicationDbContext context,
		ISecretTokenFactory tokenFactory,
		IAccessTokenIssuer accessTokenIssuer,
		RefreshTokenIssuer refreshTokenIssuer,
		TimeProvider timeProvider)
	{
		_context = context;
		_tokenFactory = tokenFactory;
		_accessTokenIssuer = accessTokenIssuer;
		_refreshTokenIssuer = refreshTokenIssuer;
		_timeProvider = timeProvider;
	}

	public async Task<AuthenticationResultDto> Handle(RedeemLoginTokenCommand request, CancellationToken cancellationToken)
	{
		string tokenHash = _tokenFactory.Hash(request.Token);
		var now = _timeProvider.GetUtcNow();

		var loginToken = await _context.LoginTokens
			.Include(token => token.User)
				.ThenInclude(user => user.Organization)
			.SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

		if (loginToken is null || !loginToken.IsRedeemable(now))
		{
			throw new AuthenticationFailedException("The sign-in link is invalid, already used, or expired.");
		}

		loginToken.Consume(now);
		loginToken.User.Activate(now);
		loginToken.User.RecordLogin(now);

		var refreshToken = _refreshTokenIssuer.StartSession(loginToken.User);

		await _context.SaveChangesAsync(cancellationToken);

		var accessToken = _accessTokenIssuer.Issue(loginToken.User);

		return new AuthenticationResultDto(
			accessToken.Value,
			"Bearer",
			accessToken.ExpiresUtc,
			refreshToken.Value,
			refreshToken.ExpiresUtc,
			UserDto.FromEntity(loginToken.User));
	}
}
