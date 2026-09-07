using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Organizations.Commands.CreateOrganization;

/// <summary>
/// Creates a new tenant. Reserved for platform administrators.
/// </summary>
[Authorize(MinimumRole = UserRole.PlatformAdministrator)]
public record CreateOrganizationCommand(string Name) : IRequest<OrganizationDto>;

public class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
{
	public CreateOrganizationCommandValidator()
	{
		RuleFor(command => command.Name)
			.NotEmpty()
			.MaximumLength(200);
	}
}

public class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, OrganizationDto>
{
	private readonly IApplicationDbContext _context;
	private readonly TimeProvider _timeProvider;

	public CreateOrganizationCommandHandler(IApplicationDbContext context, TimeProvider timeProvider)
	{
		_context = context;
		_timeProvider = timeProvider;
	}

	public async Task<OrganizationDto> Handle(CreateOrganizationCommand request, CancellationToken cancellationToken)
	{
		string name = request.Name.Trim();

		if (await _context.Organizations.AnyAsync(organization => organization.Name == name, cancellationToken))
		{
			throw new ConflictException($"An organization named \"{name}\" already exists.");
		}

		var now = _timeProvider.GetUtcNow();
		var entity = new Organization
		{
			Name = name,
			CreatedUtc = now,
			UpdatedUtc = now,
		};

		_context.Organizations.Add(entity);
		await _context.SaveChangesAsync(cancellationToken);

		return await _context.Organizations
			.AsNoTracking()
			.Where(organization => organization.Id == entity.Id)
			.Select(OrganizationDto.Projection)
			.SingleAsync(cancellationToken);
	}
}
