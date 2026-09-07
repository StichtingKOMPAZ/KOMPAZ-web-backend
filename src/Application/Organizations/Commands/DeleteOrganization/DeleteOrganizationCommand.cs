using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Organizations.Commands.DeleteOrganization;

/// <summary>
/// Removes an organization together with its users and their outstanding sign-in links.
/// </summary>
[Authorize(MinimumRole = UserRole.PlatformAdministrator)]
public record DeleteOrganizationCommand(Guid Id) : IRequest;

public class DeleteOrganizationCommandValidator : AbstractValidator<DeleteOrganizationCommand>
{
	public DeleteOrganizationCommandValidator()
	{
		RuleFor(command => command.Id)
			.NotEqual(Guid.Empty);
	}
}

public class DeleteOrganizationCommandHandler : IRequestHandler<DeleteOrganizationCommand>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _user;

	public DeleteOrganizationCommandHandler(IApplicationDbContext context, IUser user)
	{
		_context = context;
		_user = user;
	}

	public async Task Handle(DeleteOrganizationCommand request, CancellationToken cancellationToken)
	{
		var entity = await _context.Organizations
			.SingleOrDefaultAsync(organization => organization.Id == request.Id, cancellationToken)
			?? throw new NotFoundException(nameof(Organization), request.Id);

		if (_user.OrganizationId == request.Id)
		{
			throw new ConflictException("An organization cannot be deleted by one of its own members.");
		}

		_context.Organizations.Remove(entity);
		await _context.SaveChangesAsync(cancellationToken);
	}
}
