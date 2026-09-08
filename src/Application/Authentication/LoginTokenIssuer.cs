using Kompaz.Application.Common.Interfaces;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Authentication;

/// <summary>
/// Issues the single-use secret behind a sign-in link and retires any link previously sent to the same user.
/// The caller is responsible for persisting the change, so issuing a token and its side effects commit together.
/// </summary>
public sealed class LoginTokenIssuer
{
	private readonly IApplicationDbContext _context;
	private readonly ISecretTokenFactory _tokenFactory;
	private readonly IAuthenticationSettings _settings;
	private readonly TimeProvider _timeProvider;

	public LoginTokenIssuer(
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
	/// Returns the secret to email. Only its hash is added to the change tracker.
	/// </summary>
	public async Task<string> IssueAsync(User user, LoginTokenPurpose purpose, CancellationToken cancellationToken)
	{
		var now = _timeProvider.GetUtcNow();

		// Retiring the previous link is a bulk UPDATE rather than a read followed by a write, so two requests
		// arriving together cannot each conclude that the other's link does not exist yet and leave two live links
		// behind. It lands before the caller's save: should that fail there is briefly no link at all, which is the
		// safe direction to fail in, and asking for another one costs nothing.
		//
		// Only links of the same purpose are retired. An invitation is issued by an administrator, while a magic
		// link can be asked for by anybody who knows the address, so letting the second retire the first would let
		// a stranger invalidate a pending invitation as often as they liked.
		await _context.LoginTokens
			.Where(token => token.UserId == user.Id && token.Purpose == purpose && token.ConsumedUtc == null)
			.ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ConsumedUtc, (DateTimeOffset?)now), cancellationToken);

		var pair = _tokenFactory.Create();
		var lifetime = purpose == LoginTokenPurpose.Invitation
			? _settings.InvitationLifetime
			: _settings.MagicLinkLifetime;

		_context.LoginTokens.Add(LoginToken.Issue(user.Id, pair.Hash, purpose, now, lifetime));

		return pair.Value;
	}
}
