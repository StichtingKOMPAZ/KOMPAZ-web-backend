using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Organizations.Commands.CreateOrganization;

/// <summary>
/// Creates a new tenant. Reserved for platform administrators.
/// <para>
/// The logo is not part of this: it is a sub-resource of the organization that does not exist yet, uploaded with
/// <c>PUT /api/organizations/{id}/logo</c> once it does. A client that offers both in one dialog makes the two
/// calls in turn, and an upload that fails leaves an organization shown with the placeholder rather than no
/// organization at all.
/// </para>
/// </summary>
[Authorize(MinimumRole = UserRole.PlatformAdministrator)]
public record CreateOrganizationCommand(string Name) : IRequest<OrganizationDto>;

public class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
{
	public CreateOrganizationCommandValidator()
	{
		RuleFor(command => command.Name)
			.NotEmpty()
			.WithMessage(OrganizationMessages.NameRequired)
			.MaximumLength(Organization.MaximumNameLength)
			.WithMessage($"De organisatienaam mag maximaal {Organization.MaximumNameLength} tekens bevatten.");
	}
}

public class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, OrganizationDto>
{
	private readonly IApplicationDbContext _context;

	public CreateOrganizationCommandHandler(IApplicationDbContext context)
	{
		_context = context;
	}

	public async Task<OrganizationDto> Handle(CreateOrganizationCommand request, CancellationToken cancellationToken)
	{
		var entity = Organization.Create(request.Name);

		// Compared folded, and matched by the folded unique index behind it, so "Elkerliek" and "elkerliek" are
		// the same name rather than two organizations a person cannot tell apart. Losing the race between this
		// check and the insert still ends in a 409, from the index.
		if (await _context.Organizations.AnyAsync(
			organization => organization.NormalizedName == entity.NormalizedName, cancellationToken))
		{
			throw new ConflictException(OrganizationMessages.NameTaken);
		}

		_context.Organizations.Add(entity);
		await _context.SaveChangesAsync(cancellationToken);

		return await _context.Organizations
			.AsNoTracking()
			.Where(organization => organization.Id == entity.Id)
			.Select(OrganizationDto.Projection)
			.SingleAsync(cancellationToken);
	}
}
