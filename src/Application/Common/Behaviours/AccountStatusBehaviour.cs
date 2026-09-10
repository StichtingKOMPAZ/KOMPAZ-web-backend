using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;

namespace Kompaz.Application.Common.Behaviours;

/// <summary>
/// Refuses a request whose caller no longer has an account.
/// <para>
/// <strong>Why this exists.</strong> An access token is a signed claim about who somebody was when it was issued,
/// and nothing about it changes when the account does. Deleting a user removes their refresh tokens, so they cannot
/// start a new hour — but the hour they are already in would otherwise run to the end, and every authorization
/// decision in this application is made from the claims on the token. A deleted administrator could therefore have
/// carried on inviting, editing and deleting people in their organization for the rest of the access-token
/// lifetime. Deleting somebody is the one action where "in an hour" is not an acceptable answer.
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

		bool stillHere = await _context.Users
			.AsNoTracking()
			.AnyAsync(user => user.Id == userId && user.DeletedUtc == null, cancellationToken);

		if (!stillHere)
		{
			throw new AuthenticationFailedException("The account behind this token no longer exists.");
		}

		return await next();
	}
}
