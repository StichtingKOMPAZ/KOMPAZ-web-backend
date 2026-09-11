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
	private readonly ConcurrentDictionary<string, int> _linksByEmail = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentBag<string> _accountDeletedNotices = [];

	/// <summary>
	/// Gets or sets a value indicating whether sending fails, the way a relay that is down does. Set it to reach
	/// the behaviour that a delivery failure must not be visible to the caller.
	/// </summary>
	public bool DeliveryFails { get; set; }

	public Task SendMagicLinkAsync(string email, string name, string token, CancellationToken cancellationToken = default)
	{
		if (DeliveryFails)
		{
			throw new InvalidOperationException("The relay refused the message.");
		}

		Record(email, token);
		return Task.CompletedTask;
	}

	public Task SendInvitationAsync(string email, string name, string organizationName, string token, CancellationToken cancellationToken = default)
	{
		Record(email, token);
		return Task.CompletedTask;
	}

	public Task SendAccountDeletedAsync(string email, string name, CancellationToken cancellationToken = default)
	{
		if (DeliveryFails)
		{
			throw new InvalidOperationException("The relay refused the message.");
		}

		_accountDeletedNotices.Add(email);

		return Task.CompletedTask;
	}

	/// <summary>
	/// Whether the account-deleted notice went to an address.
	/// </summary>
	public bool ToldAboutDeletion(string email) =>
		_accountDeletedNotices.Contains(email, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Returns the most recent secret sent to an address, failing loudly when nothing was sent.
	/// </summary>
	public string TokenFor(string email) =>
		_tokensByEmail.TryGetValue(email, out string? token)
			? token
			: throw new InvalidOperationException($"No sign-in link was sent to {email}.");

	public bool WasSentTo(string email) => _tokensByEmail.ContainsKey(email);

	/// <summary>
	/// How many links have been sent to an address.
	/// <para>
	/// A count rather than a flag, because the question most tests actually have is whether sending a link was
	/// refused <em>this time</em> — and an address that was invited before has been sent one either way. Asking
	/// "was anything ever sent" made one test pass for a reason that had nothing to do with what it was testing.
	/// </para>
	/// </summary>
	public int LinksSentTo(string email) => _linksByEmail.TryGetValue(email, out int count) ? count : 0;

	private void Record(string email, string token)
	{
		_tokensByEmail[email] = token;
		_linksByEmail.AddOrUpdate(email, 1, (_, count) => count + 1);
	}
}
