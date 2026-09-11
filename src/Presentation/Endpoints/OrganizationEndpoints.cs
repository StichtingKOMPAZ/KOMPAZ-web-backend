using Kompaz.Application.Common.Models;
using Kompaz.Application.Organizations;
using Kompaz.Application.Organizations.Commands.CreateOrganization;
using Kompaz.Application.Organizations.Commands.DeleteOrganization;
using Kompaz.Application.Organizations.Commands.DeleteOrganizationLogo;
using Kompaz.Application.Organizations.Commands.UpdateOrganization;
using Kompaz.Application.Organizations.Commands.UploadOrganizationLogo;
using Kompaz.Application.Organizations.Queries.GetOrganization;
using Kompaz.Application.Organizations.Queries.GetOrganizationLogo;
using Kompaz.Application.Organizations.Queries.GetOrganizations;
using Kompaz.Presentation.Common.Assets;
using Kompaz.Presentation.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Kompaz.Presentation.Endpoints;

/// <summary>
/// Tenant management. Platform administrators see every organization; everyone else only their own.
/// </summary>
internal class OrganizationEndpoints : IEndpointGroup
{
	/// <summary>
	/// How much of an upload is copied at a time, the same figure <see cref="Stream.CopyToAsync(Stream)"/> uses.
	/// </summary>
	private const int CopyBufferBytes = 81920;

	public void Map(WebApplication app)
	{
		var group = app.MapEndpoints("organizations");

		group.MapGet(GetOrganizations)
			.MapGet(GetOrganization, "{id:guid}")
			.MapGet(GetOrganizationLogo, "{id:guid}/logo")
			.MapPost(CreateOrganization)
			.MapPut(UpdateOrganization, "{id:guid}")
			.MapDelete(DeleteOrganization, "{id:guid}")
			.MapDelete(DeleteOrganizationLogo, "{id:guid}/logo");

		// Mapped on its own rather than in the chain above, because it needs one call the others do not. Binding a
		// form makes ASP.NET require the anti-forgery middleware, and anti-forgery answers a question this API does
		// not have: a request is authenticated by a
		// bearer token the caller has to attach deliberately, never by a cookie a browser attaches on its own, so
		// there is no cross-site request to forge. Left on, every logo upload would fail for want of a token that
		// would protect nothing.
		group.MapPut("{id:guid}/logo", UploadOrganizationLogo)
			.WithName(nameof(UploadOrganizationLogo))
			.DisableAntiforgery();

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
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
	public async Task<Created<OrganizationDto>> CreateOrganization(ISender sender, CreateOrganizationCommand command, CancellationToken cancellationToken)
	{
		var organization = await sender.Send(command, cancellationToken);
		return TypedResults.Created($"/api/organizations/{organization.Id}", organization);
	}

	/// <summary>
	/// Renames an organization.
	/// </summary>
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
	public async Task<Ok<OrganizationDto>> UpdateOrganization(ISender sender, Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken)
	{
		var organization = await sender.Send(new UpdateOrganizationCommand(id, request.Name), cancellationToken);
		return TypedResults.Ok(organization);
	}

	/// <summary>
	/// Deletes an organization along with its users.
	/// </summary>
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
	public async Task<NoContent> DeleteOrganization(ISender sender, Guid id, CancellationToken cancellationToken)
	{
		await sender.Send(new DeleteOrganizationCommand(id), cancellationToken);
		return TypedResults.NoContent();
	}

	/// <summary>
	/// Returns the organization's logo, or the placeholder when it has none.
	/// <para>
	/// Behind the bearer token like everything else here, because which organizations exist is not public. A
	/// browser cannot put a token on an <c>&lt;img src&gt;</c>, so a client fetches this and hands the response to
	/// the element as an object URL.
	/// </para>
	/// </summary>
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
	public async Task<FileContentHttpResult> GetOrganizationLogo(
		ISender sender,
		Guid id,
		HttpResponse response,
		CancellationToken cancellationToken)
	{
		var logo = await sender.Send(new GetOrganizationLogoQuery(id), cancellationToken);

		// An SVG is a document rather than a picture: served as itself it can carry script, and this API's own
		// origin is where that script would run. These two headers are what make it harmless — the document may
		// load nothing, and the browser may not decide for itself that the bytes are something more interesting
		// than the media type says. Set for every format, because the upload chose which one this is.
		response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
		response.Headers.XContentTypeOptions = "nosniff";

		return logo is null
			? TypedResults.File(PlaceholderLogo.Content, PlaceholderLogo.ContentType)
			: TypedResults.File(logo.Content, logo.ContentType);
	}

	/// <summary>
	/// Removes the organization's logo, which puts it back to the placeholder.
	/// </summary>
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
	public async Task<NoContent> DeleteOrganizationLogo(ISender sender, Guid id, CancellationToken cancellationToken)
	{
		await sender.Send(new DeleteOrganizationLogoCommand(id), cancellationToken);
		return TypedResults.NoContent();
	}

	/// <summary>
	/// Sets or replaces the organization's logo, and answers with the organization.
	/// </summary>
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
	public async Task<Ok<OrganizationDto>> UploadOrganizationLogo(
		ISender sender,
		Guid id,
		IFormFile logo,
		CancellationToken cancellationToken)
	{
		byte[] content = await ReadAsync(logo, cancellationToken);
		var organization = await sender.Send(new UploadOrganizationLogoCommand(id, content), cancellationToken);

		return TypedResults.Ok(organization);
	}

	/// <summary>
	/// The body of a rename request. The identifier comes from the route.
	/// </summary>
	internal sealed record UpdateOrganizationRequest(string Name);

	/// <summary>
	/// Reads the upload into memory, one byte past the limit and no further.
	/// <para>
	/// That one byte is all the size rule needs, and stopping there is what keeps a caller who declares a small
	/// upload and then sends gigabytes from being believed — <see cref="IFormFile.Length"/> is the request's own
	/// claim about itself. Everything larger than the server's request-body cap never arrives here at all; this is
	/// what turns the sizes in between into a 400 naming the field rather than a closed connection.
	/// </para>
	/// </summary>
	private static async Task<byte[]> ReadAsync(IFormFile logo, CancellationToken cancellationToken)
	{
		int ceiling = LogoImage.MaximumSizeInBytes + 1;

		using var buffer = new MemoryStream();
		await using var content = logo.OpenReadStream();

		byte[] chunk = new byte[CopyBufferBytes];
		int total = 0;

		while (total < ceiling)
		{
			int read = await content.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, ceiling - total)), cancellationToken);

			if (read == 0)
			{
				break;
			}

			await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
			total += read;
		}

		return buffer.ToArray();
	}
}
