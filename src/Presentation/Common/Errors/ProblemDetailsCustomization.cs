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

		// RFC 9457 asks for a URI reference here, so the path alone. The method is already known to whoever sent it.
		problemDetails.Instance ??= httpContext.Request.Path.Value;

		if (problemDetails.Status is { } status && Defaults.TryGetValue(status, out var mapping))
		{
			// Assigned, not defaulted. The framework has already put a URI of its own here, and the point of the
			// table above is that every problem this API returns names its status from one vocabulary. The title is
			// still only a fallback, because a more specific one — "one or more validation errors" — beats the
			// status name it would otherwise be replaced with.
			problemDetails.Type = mapping.Type;
			problemDetails.Title ??= mapping.Title;
		}

		problemDetails.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
	}
}
