using Kompaz.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Kompaz.Application.Common.Behaviours;

public sealed class PerformanceBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private const long LongRunningThresholdMilliseconds = 500;

	private readonly ILogger<TRequest> _logger;
	private readonly IUser _user;

	public PerformanceBehaviour(ILogger<TRequest> logger, IUser user)
	{
		_logger = logger;
		_user = user;
	}

	public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		var timer = Stopwatch.StartNew();

		var response = await next();

		timer.Stop();

		if (timer.ElapsedMilliseconds > LongRunningThresholdMilliseconds)
		{
			_logger.LogWarning(
				"Long running request {RequestName} took {ElapsedMilliseconds} ms for user {UserId}",
				typeof(TRequest).Name,
				timer.ElapsedMilliseconds,
				_user.Id?.ToString() ?? "anonymous");
		}

		return response;
	}
}
