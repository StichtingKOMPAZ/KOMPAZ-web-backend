using Kompaz.Application.Common.Models;
using Kompaz.Application.Organizations;
using Kompaz.Application.Organizations.Commands.CreateOrganization;
using Kompaz.Application.Organizations.Commands.DeleteOrganization;
using Kompaz.Application.Organizations.Commands.UpdateOrganization;
using Kompaz.Application.Organizations.Queries.GetOrganization;
using Kompaz.Application.Organizations.Queries.GetOrganizations;
using Kompaz.Presentation.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kompaz.Presentation.Endpoints;

/// <summary>
/// Tenant management. Platform administrators see every organization; everyone else only their own.
/// </summary>
internal class OrganizationEndpoints : IEndpointGroup
{
	public void Map(WebApplication app)
	{
		var group = app.MapEndpoints("organizations");

		group.MapGet(GetOrganizations)
			.MapGet(GetOrganization, "{id:guid}")
			.MapPost(CreateOrganization)
			.MapPut(UpdateOrganization, "{id:guid}")
			.MapDelete(DeleteOrganization, "{id:guid}");

		group.RequireAuthorization();
	}

	/// <summary>
	/// Returns a page of organizations.
	/// </summary>
	public async Task<Ok<PaginatedList<OrganizationDto>>> GetOrganizations(
		ISender sender,
		CancellationToken cancellationToken,
		string? search = null,
		int pageNumber = 1,
		int pageSize = PagedQuery.DefaultPageSize)
	{
		var query = new GetOrganizationsQuery
		{
			Search = search,
			PageNumber = pageNumber,
			PageSize = pageSize,
		};

		return TypedResults.Ok(await sender.Send(query, cancellationToken));
	}

	/// <summary>
	/// Returns a single organization.
	/// </summary>
	public async Task<Ok<OrganizationDto>> GetOrganization(ISender sender, Guid id, CancellationToken cancellationToken)
	{
		var organization = await sender.Send(new GetOrganizationQuery(id), cancellationToken);
		return TypedResults.Ok(organization);
	}

	/// <summary>
	/// Creates an organization.
	/// </summary>
	public async Task<Created<OrganizationDto>> CreateOrganization(ISender sender, CreateOrganizationCommand command, CancellationToken cancellationToken)
	{
		var organization = await sender.Send(command, cancellationToken);
		return TypedResults.Created($"/api/organizations/{organization.Id}", organization);
	}

	/// <summary>
	/// Renames an organization.
	/// </summary>
	public async Task<Ok<OrganizationDto>> UpdateOrganization(ISender sender, Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken)
	{
		var organization = await sender.Send(new UpdateOrganizationCommand(id, request.Name), cancellationToken);
		return TypedResults.Ok(organization);
	}

	/// <summary>
	/// Deletes an organization along with its users.
	/// </summary>
	public async Task<NoContent> DeleteOrganization(ISender sender, Guid id, CancellationToken cancellationToken)
	{
		await sender.Send(new DeleteOrganizationCommand(id), cancellationToken);
		return TypedResults.NoContent();
	}

	/// <summary>
	/// The body of a rename request. The identifier comes from the route.
	/// </summary>
	internal sealed record UpdateOrganizationRequest(string Name);
}
