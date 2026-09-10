using Kompaz.Application.Common.Models;
using Kompaz.Application.Users;
using Kompaz.Application.Users.Commands.DeleteUser;
using Kompaz.Application.Users.Commands.InviteUser;
using Kompaz.Application.Users.Commands.ResendUserInvitation;
using Kompaz.Application.Users.Commands.RestoreUser;
using Kompaz.Application.Users.Commands.UpdateOwnProfile;
using Kompaz.Application.Users.Commands.UpdateUser;
using Kompaz.Application.Users.Queries.GetUser;
using Kompaz.Application.Users.Queries.GetUsers;
using Kompaz.Domain.Enums;
using Kompaz.Presentation.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Kompaz.Presentation.Endpoints;

/// <summary>
/// User management: invitations, the invited/active roster, and per-user reads and writes.
/// </summary>
internal class UserEndpoints : IEndpointGroup
{
	public void Map(WebApplication app)
	{
		var group = app.MapEndpoints("users");

		group.MapGet(GetUsers)
			.MapGet(GetUser, "{id:guid}")
			.MapPost(InviteUser, "invitations")
			.MapPost(ResendUserInvitation, "{id:guid}/invitations")
			.MapPost(RestoreUser, "{id:guid}/restore")
			.MapPut(UpdateOwnProfile, "me")
			.MapPut(UpdateUser, "{id:guid}")
			.MapDelete(DeleteUser, "{id:guid}");

		group.RequireAuthorization();
	}

	/// <summary>
	/// Returns a page of users, optionally limited to those still invited or already active. Deleted users are left
	/// out unless <c>includeDeleted</c> asks for them, which is how an administrator finds one to restore.
	/// </summary>
	public async Task<Ok<PaginatedList<UserDto>>> GetUsers(
		ISender sender,
		CancellationToken cancellationToken,
		UserStatus? status = null,
		string? search = null,
		Guid? organizationId = null,
		bool includeDeleted = false,
		int pageNumber = 1,
		int pageSize = PagedQuery.DefaultPageSize)
	{
		var query = new GetUsersQuery
		{
			Status = status,
			Search = search,
			OrganizationId = organizationId,
			IncludeDeleted = includeDeleted,
			PageNumber = pageNumber,
			PageSize = pageSize,
		};

		return TypedResults.Ok(await sender.Send(query, cancellationToken));
	}

	/// <summary>
	/// Returns a single user. Deleted users are not found here; they are listed by the roster on request.
	/// </summary>
	public async Task<Ok<UserDto>> GetUser(ISender sender, Guid id, CancellationToken cancellationToken)
	{
		var user = await sender.Send(new GetUserQuery(id), cancellationToken);
		return TypedResults.Ok(user);
	}

	/// <summary>
	/// Invites someone into an organization and emails them a link that accepts the invitation and signs them in.
	/// </summary>
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
	public async Task<Created<UserDto>> InviteUser(ISender sender, InviteUserCommand command, CancellationToken cancellationToken)
	{
		var user = await sender.Send(command, cancellationToken);
		return TypedResults.Created($"/api/users/{user.Id}", user);
	}

	/// <summary>
	/// Sends a fresh invitation link, retiring the previous one.
	/// </summary>
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
	public async Task<Accepted> ResendUserInvitation(ISender sender, Guid id, CancellationToken cancellationToken)
	{
		await sender.Send(new ResendUserInvitationCommand(id), cancellationToken);
		return TypedResults.Accepted((string?)null);
	}

	/// <summary>
	/// Updates the signed-in user's own profile. Open to any role, unlike the administrator edit below.
	/// </summary>
	public async Task<Ok<UserDto>> UpdateOwnProfile(ISender sender, UpdateOwnProfileCommand command, CancellationToken cancellationToken)
	{
		var user = await sender.Send(command, cancellationToken);
		return TypedResults.Ok(user);
	}

	/// <summary>
	/// Updates a user's display name and role, as an administrator.
	/// </summary>
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
	public async Task<Ok<UserDto>> UpdateUser(ISender sender, Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
	{
		var user = await sender.Send(new UpdateUserCommand(id, request.Name, request.Role), cancellationToken);
		return TypedResults.Ok(user);
	}

	/// <summary>
	/// Deletes a user. They lose access immediately and are emailed about it; an administrator can undo this with
	/// the restore endpoint.
	/// </summary>
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
	public async Task<NoContent> DeleteUser(ISender sender, Guid id, CancellationToken cancellationToken)
	{
		await sender.Send(new DeleteUserCommand(id), cancellationToken);
		return TypedResults.NoContent();
	}

	/// <summary>
	/// Restores a deleted user, leaving one who is not deleted as they are. Their sign-in links and sessions are
	/// not restored with them, so they sign in again from the login page.
	/// </summary>
	public async Task<Ok<UserDto>> RestoreUser(ISender sender, Guid id, CancellationToken cancellationToken)
	{
		var user = await sender.Send(new RestoreUserCommand(id), cancellationToken);
		return TypedResults.Ok(user);
	}

	/// <summary>
	/// The body of a user update. The identifier comes from the route.
	/// </summary>
	internal sealed record UpdateUserRequest(string Name, UserRole Role);
}
