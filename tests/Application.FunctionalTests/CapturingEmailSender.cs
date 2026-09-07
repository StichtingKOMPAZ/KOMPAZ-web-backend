using Kompaz.Application.Common.Interfaces;
using System.Collections.Concurrent;

namespace Kompaz.Application.FunctionalTests;

/// <summary>
/// Stands in for the real email transport and keeps the sign-in secrets that would have been sent, so a test can
/// follow the same link a user would click.
/// </summary>
internal sealed class CapturingEmailSender : IAuthenticationEmailSender
{
	private readonly ConcurrentDictionary<string, string> _tokensByEmail = new(StringComparer.OrdinalIgnoreCase);

	public Task SendMagicLinkAsync(string email, string name, string token, CancellationToken cancellationToken = default)
	{
		_tokensByEmail[email] = token;
		return Task.CompletedTask;
	}

	public Task SendInvitationAsync(string email, string name, string organizationName, string token, CancellationToken cancellationToken = default)
	{
		_tokensByEmail[email] = token;
		return Task.CompletedTask;
	}

	/// <summary>
	/// Returns the most recent secret sent to an address, failing loudly when nothing was sent.
	/// </summary>
	public string TokenFor(string email) =>
		_tokensByEmail.TryGetValue(email, out string? token)
			? token
			: throw new InvalidOperationException($"No sign-in link was sent to {email}.");

	public bool WasSentTo(string email) => _tokensByEmail.ContainsKey(email);
}
