using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using Kompaz.Domain.Events;

namespace Kompaz.Application.Organizations.Commands.DeleteOrganization;

/// <summary>
/// Removes an organization together with its users, its logo, and their outstanding sign-in links.
/// <para>
/// A real deletion, not the marked one <c>DeleteUserCommand</c> performs, because this is what the MVP asks for:
/// the organization and everybody in it go. That is worth knowing before reaching for it — the rows are gone, so
/// there is nothing to restore, and the audit trail on other tables loses the identifiers it pointed at.
/// </para>
/// <para>
/// Everyone who had an account is told by email. The notices are raised as events on the organization and so are
/// sent after the deletion is committed, which means a relay that is down costs the notice and not the deletion.
/// </para>
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

		// Said out loud rather than left to follow from something else. Every platform administrator belongs to
		// this organization, so the check below already refuses today; that is a consequence of where the role is
		// held, and the day it stops being true is not the day to discover that this request would take every
		// administrator, every tenant's owner and the seeder's answer to "is the database empty" with it.
		if (entity.IsPlatform)
		{
			throw new ConflictException(
				"De organisatie die het platform beheert kan niet worden verwijderd.");
		}

		if (_user.OrganizationId == request.Id)
		{
			throw new ConflictException(
				"Een organisatie kan niet worden verwijderd door een van haar eigen leden.");
		}

		// Read before the delete, because after it there is nobody left to ask. Only the addresses and names are
		// read: what the notice needs, rather than rows that are about to stop existing. Users already deleted are
		// left out — they were told when it happened, and telling them again would be news about an account they
		// no longer have.
		var members = await _context.Users
			.AsNoTracking()
			.Where(user => user.OrganizationId == request.Id && user.DeletedUtc == null)
			.Select(user => new { user.Id, user.Email, user.Name })
			.ToListAsync(cancellationToken);

		// Raised on the organization rather than on each user: the users are removed by the cascade behind this
		// row, so they are not tracked to raise anything of their own. It is still the account-deleted notice
		// they get, because from the recipient's side that is exactly what happened.
		foreach (var member in members)
		{
			entity.AddDomainEvent(new UserDeletedEvent(member.Id, member.Email, member.Name));
		}

		// The logo row goes with the organization on the cascade, which means nothing would be left to raise the
		// removal of its file. Asked for here and raised on the organization instead — the same reason the
		// account notices above are.
		string? logoKey = await _context.OrganizationLogos
			.Where(logo => logo.OrganizationId == request.Id)
			.Select(logo => logo.StorageKey)
			.SingleOrDefaultAsync(cancellationToken);

		if (logoKey is not null)
		{
			entity.AddDomainEvent(new OrganizationLogoDiscardedEvent(logoKey));
		}

		_context.Organizations.Remove(entity);
		await _context.SaveChangesAsync(cancellationToken);
	}
}
