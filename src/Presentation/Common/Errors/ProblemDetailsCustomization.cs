using System.Diagnostics;

namespace Kompaz.Presentation.Common.Errors;

/// <summary>
/// Applies RFC 9457 (Problem Details for HTTP APIs) members that the framework does not populate by default:
/// a stable <c>type</c> URI, a <c>title</c>, the request <c>instance</c>, and a correlation <c>traceId</c>.
/// </summary>
internal static class ProblemDetailsCustomization
{
	private static readonly Dictionary<int, (string Type, string Title)> Defaults = new()
	{
		[StatusCodes.Status400BadRequest] = ("https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1", "Bad Request"),
		[StatusCodes.Status401Unauthorized] = ("https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.2", "Unauthorized"),
		[StatusCodes.Status403Forbidden] = ("https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.4", "Forbidden"),
		[StatusCodes.Status404NotFound] = ("https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.5", "Not Found"),
		[StatusCodes.Status409Conflict] = ("https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.10", "Conflict"),
		[StatusCodes.Status429TooManyRequests] = ("https://datatracker.ietf.org/doc/html/rfc6585#section-4", "Too Many Requests"),
		[StatusCodes.Status500InternalServerError] = ("https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1", "An error occurred while processing your request."),
	};

	public static void Apply(ProblemDetailsContext context)
	{
		var problemDetails = context.ProblemDetails;
		var httpContext = context.HttpContext;

		problemDetails.Status ??= httpContext.Response.StatusCode;
		problemDetails.Instance ??= $"{httpContext.Request.Method} {httpContext.Request.Path}";

		if (problemDetails.Status is { } status && Defaults.TryGetValue(status, out var mapping))
		{
			problemDetails.Type ??= mapping.Type;
			problemDetails.Title ??= mapping.Title;
		}

		problemDetails.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
	}
}
