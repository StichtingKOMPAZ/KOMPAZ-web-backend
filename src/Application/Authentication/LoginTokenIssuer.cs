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

		var outstanding = await _context.LoginTokens
			.Where(token => token.UserId == user.Id && token.ConsumedUtc == null)
			.ToListAsync(cancellationToken);

		foreach (var token in outstanding)
		{
			token.Consume(now);
		}

		var pair = _tokenFactory.Create();
		var lifetime = purpose == LoginTokenPurpose.Invitation
			? _settings.InvitationLifetime
			: _settings.MagicLinkLifetime;

		_context.LoginTokens.Add(LoginToken.Issue(user.Id, pair.Hash, purpose, now, lifetime));

		return pair.Value;
	}
}
