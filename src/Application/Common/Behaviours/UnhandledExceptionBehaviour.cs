using Kompaz.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace Kompaz.Application.Common.Behaviours;

public sealed class UnhandledExceptionBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly ILogger<TRequest> _logger;

	public UnhandledExceptionBehaviour(ILogger<TRequest> logger)
	{
		_logger = logger;
	}

	public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		try
		{
			return await next();
		}
		catch (Exception exception) when (exception
			is not Kompaz.Application.Common.Exceptions.ValidationException
			and not NotFoundException
			and not ConflictException
			and not AuthenticationFailedException
			and not ForbiddenAccessException
			and not UnauthorizedAccessException)
		{
			_logger.LogError(exception, "Unhandled exception for request {RequestName}", typeof(TRequest).Name);
			throw;
		}
	}
}
