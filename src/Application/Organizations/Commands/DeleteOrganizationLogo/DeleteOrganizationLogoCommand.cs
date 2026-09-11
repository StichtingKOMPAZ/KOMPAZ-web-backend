using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Organizations.Commands.DeleteOrganizationLogo;

/// <summary>
/// Takes an organization's logo away, which puts it back to the placeholder. Administrators may only do so for
/// their own, the same reach they have over its name and over setting the logo in the first place.
/// <para>
/// The row goes in the transaction and the file goes after it, on the event the row raises. Removing the file
/// first would mean a failed save left the organization pointing at nothing.
/// </para>
/// </summary>
[Authorize(MinimumRole = UserRole.Administrator)]
public record DeleteOrganizationLogoCommand(Guid OrganizationId) : IRequest;

public class DeleteOrganizationLogoCommandValidator : AbstractValidator<DeleteOrganizationLogoCommand>
{
	public DeleteOrganizationLogoCommandValidator()
	{
		RuleFor(command => command.OrganizationId)
			.NotEqual(Guid.Empty);
	}
}

public class DeleteOrganizationLogoCommandHandler : IRequestHandler<DeleteOrganizationLogoCommand>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _user;

	public DeleteOrganizationLogoCommandHandler(IApplicationDbContext context, IUser user)
	{
		_context = context;
		_user = user;
	}

	public async Task Handle(DeleteOrganizationLogoCommand request, CancellationToken cancellationToken)
	{
		OrganizationAccess.EnsureCanManage(_user, request.OrganizationId);

		// Asked before the logo, so an organization that is not there and an organization with no logo are
		// different answers rather than the same 404 about the wrong thing.
		if (!await _context.Organizations.AnyAsync(
			organization => organization.Id == request.OrganizationId, cancellationToken))
		{
			throw new NotFoundException(nameof(Organization), request.OrganizationId);
		}

		// Loaded rather than deleted where it sits, because the row is what knows the key and what raises the
		// event carrying it. It is a few short columns now that the image itself lives elsewhere.
		var logo = await _context.OrganizationLogos
			.SingleOrDefaultAsync(candidate => candidate.OrganizationId == request.OrganizationId, cancellationToken)
			?? throw new NotFoundException(nameof(OrganizationLogo), request.OrganizationId);

		logo.Discard();
		_context.OrganizationLogos.Remove(logo);

		await _context.SaveChangesAsync(cancellationToken);
	}
}
