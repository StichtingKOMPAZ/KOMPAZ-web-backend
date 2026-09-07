using Kompaz.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Kompaz.Presentation.Infrastructure;

/// <summary>
/// Translates the application's failure types into RFC 9457 problem responses.
/// </summary>
internal sealed class CustomExceptionHandler : IExceptionHandler
{
	private readonly IProblemDetailsService _problemDetailsService;

	public CustomExceptionHandler(IProblemDetailsService problemDetailsService)
	{
		_problemDetailsService = problemDetailsService;
	}

	public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
	{
		ProblemDetails? problemDetails = exception switch
		{
			ValidationException validationException => new ValidationProblemDetails(validationException.Errors)
			{
				Status = StatusCodes.Status400BadRequest
			},
			NotFoundException => new ProblemDetails
			{
				Status = StatusCodes.Status404NotFound,
				Detail = exception.Message
			},
			ConflictException => new ProblemDetails
			{
				Status = StatusCodes.Status409Conflict,
				Detail = exception.Message
			},
			AuthenticationFailedException => new ProblemDetails
			{
				Status = StatusCodes.Status401Unauthorized,
				Detail = exception.Message
			},
			UnauthorizedAccessException => new ProblemDetails
			{
				Status = StatusCodes.Status401Unauthorized
			},
			ForbiddenAccessException => new ProblemDetails
			{
				Status = StatusCodes.Status403Forbidden,
				Detail = exception.Message
			},
			_ => null
		};

		if (problemDetails is null)
		{
			return false;
		}

		httpContext.Response.StatusCode = problemDetails.Status!.Value;

		return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
		{
			HttpContext = httpContext,
			Exception = exception,
			ProblemDetails = problemDetails
		});
	}
}
