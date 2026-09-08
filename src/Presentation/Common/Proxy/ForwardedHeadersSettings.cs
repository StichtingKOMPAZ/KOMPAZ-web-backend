using System.Net;

namespace Kompaz.Presentation.Common.Proxy;

/// <summary>
/// Which proxies this application believes when they say who the caller is, bound from the "ForwardedHeaders" section.
/// <para>
/// Every per-client decision keys on the connection's address, and behind a reverse proxy that address is the proxy
/// rather than the caller. Left unconfigured, one client IP therefore stands for everybody: the whole API shares a
/// single rate-limit partition, and <c>X-Forwarded-Proto</c> goes unread, so HTTPS redirection bounces requests the
/// proxy already terminated TLS for.
/// </para>
/// <para>
/// Believing the wrong sender is worse than believing none. A caller who can reach the application directly could
/// claim a fresh address on every request and never meet a rate limit, so nothing is believed unless it is named
/// here. That is also why the framework's own default — believe loopback, silently — is not kept: it is neither
/// off nor configured, and it looks like it works right up until the proxy is not on localhost.
/// </para>
/// </summary>
internal sealed class ForwardedHeadersSettings
{
	public const string SectionName = "ForwardedHeaders";

	/// <summary>
	/// Gets the individual proxy addresses to believe.
	/// </summary>
	public string[] KnownProxies { get; init; } = [];

	/// <summary>
	/// Gets the proxy networks to believe, in CIDR notation, such as <c>10.0.0.0/8</c>.
	/// </summary>
	public string[] KnownNetworks { get; init; } = [];

	/// <summary>
	/// Gets a value indicating whether to believe forwarded headers from any address, for a deployment whose proxy
	/// address is not known ahead of time. Safe only where the application cannot be reached except through that
	/// proxy, and it says so in the log at startup.
	/// </summary>
	public bool TrustAnyProxy { get; init; }

	/// <summary>
	/// Gets how many chained proxies to walk back through. One, unless proxies are nested.
	/// </summary>
	public int ForwardLimit { get; init; } = 1;

	/// <summary>
	/// Gets a value indicating whether any proxy has been named, and so whether the middleware runs at all.
	/// </summary>
	public bool IsEnabled => TrustAnyProxy || KnownProxies.Length > 0 || KnownNetworks.Length > 0;

	/// <summary>
	/// Reports the first configuration problem that would leave the caller's address wrong.
	/// </summary>
	public string? Validate()
	{
		if (TrustAnyProxy && (KnownProxies.Length > 0 || KnownNetworks.Length > 0))
		{
			return $"{SectionName}:{nameof(TrustAnyProxy)} already believes every address, so it cannot be combined "
				+ $"with {nameof(KnownProxies)} or {nameof(KnownNetworks)}. Name the proxies, or trust any, not both.";
		}

		if (IsEnabled && ForwardLimit < 1)
		{
			return $"{SectionName}:{nameof(ForwardLimit)} must be at least 1.";
		}

		foreach (string proxy in KnownProxies)
		{
			if (!IPAddress.TryParse(proxy, out _))
			{
				return $"{SectionName}:{nameof(KnownProxies)} contains \"{proxy}\", which is not an IP address.";
			}
		}

		foreach (string network in KnownNetworks)
		{
			if (!IPNetwork.TryParse(network, out _))
			{
				return $"{SectionName}:{nameof(KnownNetworks)} contains \"{network}\", which is not a CIDR network "
					+ "such as 10.0.0.0/8.";
			}
		}

		return null;
	}
}
