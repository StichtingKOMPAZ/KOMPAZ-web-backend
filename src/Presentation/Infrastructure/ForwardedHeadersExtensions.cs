using Kompaz.Presentation.Common.Proxy;
using Microsoft.Extensions.Options;

namespace Kompaz.Presentation.Infrastructure;

internal static class ForwardedHeadersExtensions
{
	/// <summary>
	/// Rewrites the scheme and the caller's address from the forwarded headers, ahead of every middleware that
	/// reads either — HTTPS redirection and the rate limiter above all. Runs only when a proxy has been named:
	/// with none, the connection's own address is the honest answer.
	/// </summary>
	public static WebApplication UseConfiguredForwardedHeaders(this WebApplication app)
	{
		var settings = app.Services.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value;

		if (!settings.IsEnabled)
		{
			return app;
		}

		app.UseForwardedHeaders();

		if (settings.TrustAnyProxy)
		{
			app.Logger.LogWarning(
				"Forwarded headers are believed from any address. Anything derived from the caller's address — the "
				+ "rate-limit partition, logged client addresses — can be chosen by the caller unless this "
				+ "application is unreachable except through its proxy.");
		}

		return app;
	}
}
