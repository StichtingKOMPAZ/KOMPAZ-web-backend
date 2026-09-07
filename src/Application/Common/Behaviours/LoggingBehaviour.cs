using Kompaz.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Kompaz.Application.Common.Behaviours;

public sealed class LoggingBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly ILogger<TRequest> _logger;
	private readonly IUser _user;

	public LoggingBehaviour(ILogger<TRequest> logger, IUser user)
	{
		_logger = logger;
		_user = user;
	}

	public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		if (_logger.IsEnabled(LogLevel.Information))
		{
			_logger.LogInformation(
				"Handling request {RequestName} for user {UserId}",
				typeof(TRequest).Name,
				_user.Id?.ToString() ?? "anonymous");
		}

		return next();
	}
}
