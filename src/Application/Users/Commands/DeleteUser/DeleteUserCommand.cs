using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;
using Kompaz.Domain.Events;

namespace Kompaz.Application.Users.Commands.DeleteUser;

/// <summary>
/// Deletes a user: they lose access at once and are told by email, and an administrator can undo it.
/// <para>
/// The row is kept and marked rather than removed. Three things need it to survive: restoring the user has to know
/// what to put back, the audit trail on every other table refers to people by identifier, and the address is the
/// unique key — so removing the row for real would burn the email address and orphan the history at the same time.
/// Their credentials are the exception and are deleted outright, because a link already sitting in an inbox has to
/// stop working the moment the account does.
/// </para>
/// </summary>
[Authorize(MinimumRole = UserRole.Administrator)]
public record DeleteUserCommand(Guid Id) : IRequest;

public class DeleteUserCommandValidator : AbstractValidator<DeleteUserCommand>
{
	public DeleteUserCommandValidator()
	{
		RuleFor(command => command.Id)
			.NotEqual(Guid.Empty);
	}
}

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
	private readonly IApplicationDbContext _context;
	private readonly IUser _currentUser;
	private readonly TimeProvider _timeProvider;

	public DeleteUserCommandHandler(IApplicationDbContext context, IUser currentUser, TimeProvider timeProvider)
	{
		_context = context;
		_currentUser = currentUser;
		_timeProvider = timeProvider;
	}

	public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
	{
		// A user who is already deleted is gone as far as every other endpoint is concerned, so this one agrees
		// with them rather than reporting a second deletion as a success.
		var user = await _context.Users
			.SingleOrDefaultAsync(candidate => candidate.Id == request.Id && candidate.DeletedUtc == null, cancellationToken)
			?? throw new NotFoundException(nameof(User), request.Id);

		OrganizationAccess.EnsureCanManage(_currentUser, user.OrganizationId);
		OrganizationAccess.EnsureCanManageRole(_currentUser, user.Role);

		if (_currentUser.Id == user.Id)
		{
			throw new ConflictException("Een gebruiker kan het eigen account niet verwijderen.");
		}

		if (user.Role >= UserRole.Administrator)
		{
			await AdministratorCoverage.EnsureAnAdministratorRemainsAsync(
				_context, user.OrganizationId, user.Id, cancellationToken);
		}

		var now = _timeProvider.GetUtcNow();

		// Deleted outright, not marked. These are credentials: a sign-in link in an inbox or a refresh token in a
		// browser would otherwise still be presentable, and the row they hang off is exactly what the checks that
		// would catch that are looking at. Restoring the user does not bring them back — they sign in again.
		await _context.LoginTokens
			.Where(token => token.UserId == user.Id)
			.ExecuteDeleteAsync(cancellationToken);

		await _context.RefreshTokens
			.Where(token => token.UserId == user.Id)
			.ExecuteDeleteAsync(cancellationToken);

		user.Delete(now);
		user.AddDomainEvent(new UserDeletedEvent(user.Id, user.Email, user.Name));

		await _context.SaveChangesAsync(cancellationToken);
	}
}
