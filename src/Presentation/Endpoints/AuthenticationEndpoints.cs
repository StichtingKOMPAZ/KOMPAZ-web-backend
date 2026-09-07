using Kompaz.Application.Authentication;
using Kompaz.Application.Authentication.Commands.RedeemLoginToken;
using Kompaz.Application.Authentication.Commands.RefreshAccessToken;
using Kompaz.Application.Authentication.Commands.RequestMagicLink;
using Kompaz.Application.Authentication.Commands.RevokeRefreshToken;
using Kompaz.Application.Authentication.Queries.GetCurrentUser;
using Kompaz.Application.Users;
using Kompaz.Presentation.Common.RateLimiting;
using Kompaz.Presentation.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kompaz.Presentation.Endpoints;

/// <summary>
/// Passwordless sign-in: request a link by email, then exchange the link's token for an access token.
/// </summary>
internal class AuthenticationEndpoints : IEndpointGroup
{
	public void Map(WebApplication app)
	{
		var group = app.MapEndpoints("auth");

		// The anonymous endpoints send email and accept secrets, so they get a tighter budget than the rest of the API.
		group.MapGroup(string.Empty)
			.RequireRateLimiting(RateLimitSettings.SignInPolicyName)
			.MapPost(RequestMagicLink, "magic-link")
			.MapPost(RedeemLoginToken, "tokens")
			.MapPost(RefreshAccessToken, "tokens/refresh")
			.MapPost(RevokeRefreshToken, "tokens/revoke");

		group.MapGroup(string.Empty)
			.RequireAuthorization()
			.MapGet(GetCurrentUser, "me");
	}

	/// <summary>
	/// Emails a sign-in link. Always accepted, whether or not the address belongs to an account.
	/// </summary>
	public async Task<Accepted> RequestMagicLink(ISender sender, RequestMagicLinkCommand command, CancellationToken cancellationToken)
	{
		await sender.Send(command, cancellationToken);
		return TypedResults.Accepted((string?)null);
	}

	/// <summary>
	/// Exchanges the single-use token from a sign-in link for a bearer token, activating an invited user.
	/// </summary>
	public async Task<Ok<AuthenticationResultDto>> RedeemLoginToken(ISender sender, RedeemLoginTokenCommand command, CancellationToken cancellationToken)
	{
		var result = await sender.Send(command, cancellationToken);
		return TypedResults.Ok(result);
	}

	/// <summary>
	/// Exchanges a refresh token for a new access token and its successor, sliding the session forward. The refresh
	/// token supplied is spent by this call; replaying it ends the session.
	/// </summary>
	public async Task<Ok<AuthenticationResultDto>> RefreshAccessToken(ISender sender, RefreshAccessTokenCommand command, CancellationToken cancellationToken)
	{
		var result = await sender.Send(command, cancellationToken);
		return TypedResults.Ok(result);
	}

	/// <summary>
	/// Signs out by ending the session a refresh token belongs to. Succeeds even for a token that is already gone,
	/// so a client can always clear its credentials.
	/// </summary>
	public async Task<NoContent> RevokeRefreshToken(ISender sender, RevokeRefreshTokenCommand command, CancellationToken cancellationToken)
	{
		await sender.Send(command, cancellationToken);
		return TypedResults.NoContent();
	}

	/// <summary>
	/// Returns the profile behind the bearer token on this request.
	/// </summary>
	public async Task<Ok<UserDto>> GetCurrentUser(ISender sender, CancellationToken cancellationToken)
	{
		var user = await sender.Send(new GetCurrentUserQuery(), cancellationToken);
		return TypedResults.Ok(user);
	}
}
