using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;

namespace Kompaz.Application.Common.Behaviours;

/// <summary>
/// Refuses a request whose caller no longer has the account, role or organization their token claims.
/// <para>
/// <strong>Why this exists.</strong> An access token is a signed claim about who somebody was when it was issued,
/// and nothing about it changes when the account does. Deleting a user removes their refresh tokens, so they cannot
/// start a new hour — but the hour they are already in would otherwise run to the end, and every authorization
/// decision in this application is made from the claims on the token. A deleted administrator could therefore have
/// carried on inviting, editing and deleting people in their organization for the rest of the access-token
/// lifetime. Deleting somebody is the one action where "in an hour" is not an acceptable answer.
/// </para>
/// <para>
/// <strong>The same is true of what the token says about them.</strong> Every authorization decision is made from
/// the <c>role</c> and <c>organizationId</c> claims, which were true when the token was signed and are not
/// revisited. Demoting an administrator would otherwise leave them administering for the rest of the hour, and
/// moving somebody would leave them acting inside the organization they just left. Comparing the two here is what
/// makes an edit take effect now. The rejection is a 401 rather than a 403 because the token is the thing that is
/// wrong, not the request: a client that still holds a refresh token exchanges it and comes straight back with
/// claims that match the row, so a demotion costs one round trip rather than a sign-in.
/// </para>
/// <para>
/// The cost is one lookup by primary key on each authenticated request. That is the price of not keeping a
/// revocation list, and it is paid alongside the queries the handler is about to run anyway.
/// </para>
/// <para>
/// Anonymous requests pass straight through: the sign-in endpoints have no caller to check, and the ones that do
/// have a credential — a sign-in link, a refresh token — are answered by rows that deleting a user removes.
/// </para>
/// </summary>
/// <typeparam name="TRequest">The request being handled.</typeparam>
/// <typeparam name="TResponse">The response the handler produces.</typeparam>
public sealed class AccountStatusBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly IUser _user;
	private readonly IApplicationDbContext _context;

	public AccountStatusBehaviour(IUser user, IApplicationDbContext context)
	{
		_user = user;
		_context = context;
	}

	public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		if (_user.Id is not { } userId)
		{
			return await next();
		}

		var account = await _context.Users
			.AsNoTracking()
			.Where(user => user.Id == userId && user.DeletedUtc == null)
			.Select(user => new { user.Role, user.OrganizationId })
			.SingleOrDefaultAsync(cancellationToken)
			?? throw new AuthenticationFailedException("The account behind this token no longer exists.");

		if (_user.Role != account.Role || _user.OrganizationId != account.OrganizationId)
		{
			throw new AuthenticationFailedException(
				"This token was issued for a role or organization the account no longer has.");
		}

		return await next();
	}
}
