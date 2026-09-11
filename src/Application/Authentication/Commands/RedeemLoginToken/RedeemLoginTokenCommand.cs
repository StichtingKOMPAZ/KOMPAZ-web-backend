using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Application.Users;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Authentication.Commands.RedeemLoginToken;

/// <summary>
/// Exchanges the secret from a sign-in link for an access token. Redeeming a link also activates an invited user.
/// </summary>
/// <remarks>
/// Anonymous by necessity: the secret from the link is the credential being presented.
/// </remarks>
[AllowAnonymous]
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

		// Every reason to refuse — unknown, spent, expired — is one condition of a single UPDATE, so the link is
		// either claimed by this request or not claimed at all. Reading it first and then spending it would let two
		// requests carrying the same secret both pass the read and both open a session.
		int claimed = await _context.LoginTokens
			.Where(token => token.TokenHash == tokenHash && token.ConsumedUtc == null && token.ExpiresUtc > now)
			.ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ConsumedUtc, (DateTimeOffset?)now), cancellationToken);

		if (claimed == 0)
		{
			throw new AuthenticationFailedException("Deze inloglink is ongeldig, al gebruikt of verlopen.");
		}

		var loginToken = await _context.LoginTokens
			.Include(token => token.User)
				.ThenInclude(user => user.Organization)
			.SingleAsync(token => token.TokenHash == tokenHash, cancellationToken);

		bool accepting = loginToken.User.Status == UserStatus.Invited;

		loginToken.User.Activate(now);
		loginToken.User.RecordLogin(now);

		if (accepting)
		{
			// The invitation has been accepted now, whichever link the invitee actually arrived on — a magic link
			// they asked for themselves activates them just as well. Any invitation still outstanding is therefore
			// spent: leaving it redeemable would keep a week-long credential alive in an inbox for somebody who can
			// already sign in, where a magic link only ever lives thirty minutes, and would leave the roster
			// reporting an invitation nobody is waiting on. Like issuing a link, this lands before the caller's
			// save, so a failure leaves the link retired rather than live.
			await _context.LoginTokens
				.Where(token => token.UserId == loginToken.UserId
					&& token.Purpose == LoginTokenPurpose.Invitation
					&& token.ConsumedUtc == null)
				.ExecuteUpdateAsync(
					setters => setters.SetProperty(token => token.ConsumedUtc, (DateTimeOffset?)now),
					cancellationToken);
		}

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
