using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Organizations.Commands.UpdateOrganization;

/// <summary>
/// Renames an organization. Administrators may only rename their own.
/// </summary>
[Authorize(MinimumRole = UserRole.Administrator)]
public record UpdateOrganizationCommand(Guid Id, string Name) : IRequest<OrganizationDto>;

public class UpdateOrganizationCommandValidator : AbstractValidator<UpdateOrganizationCommand>
{
	public UpdateOrganizationCommandValidator()
	{
		RuleFor(command => command.Id)
			.NotEqual(Guid.Empty);

		RuleFor(command => command.Name)
			.NotEmpty()
			.MaximumLength(200);
	}
}

public class UpdateOrganizationCommandHandler : IRequestHandler<UpdateOrganizationCommand, OrganizationDto>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _user;

	public UpdateOrganizationCommandHandler(IApplicationDbContext context, IUser user)
	{
		_context = context;
		_user = user;
	}

	public async Task<OrganizationDto> Handle(UpdateOrganizationCommand request, CancellationToken cancellationToken)
	{
		OrganizationAccess.EnsureCanManage(_user, request.Id);

		var entity = await _context.Organizations
			.SingleOrDefaultAsync(organization => organization.Id == request.Id, cancellationToken)
			?? throw new NotFoundException(nameof(Organization), request.Id);

		string name = request.Name.Trim();

		bool nameTaken = await _context.Organizations
			.AnyAsync(organization => organization.Id != request.Id && organization.Name == name, cancellationToken);

		if (nameTaken)
		{
			throw new ConflictException($"An organization named \"{name}\" already exists.");
		}

		entity.Rename(name);
		await _context.SaveChangesAsync(cancellationToken);

		return await _context.Organizations
			.AsNoTracking()
			.Where(organization => organization.Id == entity.Id)
			.Select(OrganizationDto.Projection)
			.SingleAsync(cancellationToken);
	}
}
