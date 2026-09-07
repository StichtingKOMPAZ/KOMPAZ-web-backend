using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using System.Reflection;

namespace Kompaz.Application.Common.Behaviours;

/// <summary>
/// Rejects requests marked with <see cref="AuthorizeAttribute"/> when the caller is anonymous or lacks the required role.
/// </summary>
/// <typeparam name="TRequest">The request being handled.</typeparam>
/// <typeparam name="TResponse">The response the handler produces.</typeparam>
public sealed class AuthorizationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly IUser _user;

	public AuthorizationBehaviour(IUser user)
	{
		_user = user;
	}

	public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		var attribute = request.GetType().GetCustomAttribute<AuthorizeAttribute>();

		if (attribute is null)
		{
			return next();
		}

		if (_user.Id is null || _user.Role is not { } role)
		{
			throw new UnauthorizedAccessException();
		}

		if (role < attribute.MinimumRole)
		{
			throw new ForbiddenAccessException($"The request requires the {attribute.MinimumRole} role.");
		}

		return next();
	}
}
