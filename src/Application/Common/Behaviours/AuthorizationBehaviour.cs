using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using System.Reflection;

namespace Kompaz.Application.Common.Behaviours;

/// <summary>
/// Rejects requests marked with <see cref="AuthorizeAttribute"/> when the caller is anonymous or lacks the required
/// role, and rejects requests marked with neither that nor <see cref="AllowAnonymousAttribute"/> outright.
/// <para>
/// That last part is the point: a new request whose author did not think about authorization fails rather than being
/// published to anybody who can reach the endpoint. A code-style test catches the omission long before this does,
/// but only for public types, so the closed door stays here too.
/// </para>
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
		var requestType = request.GetType();
		var attribute = requestType.GetCustomAttribute<AuthorizeAttribute>();

		if (attribute is null)
		{
			if (requestType.GetCustomAttribute<AllowAnonymousAttribute>() is null)
			{
				throw new ForbiddenAccessException(
					$"{requestType.Name} carries neither {nameof(AuthorizeAttribute)} nor "
					+ $"{nameof(AllowAnonymousAttribute)}, so who may execute it has not been decided.");
			}

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
