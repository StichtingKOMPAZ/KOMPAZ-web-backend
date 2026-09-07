using Kompaz.Application.Common.Models;

namespace Kompaz.Application.Common.Interfaces;

/// <summary>
/// Creates and verifies the opaque secrets the system hands out: the ones emailed as sign-in links, and the refresh
/// tokens a signed-in client holds. Only hashes are ever stored.
/// </summary>
public interface ISecretTokenFactory
{
	/// <summary>
	/// Creates a new secret together with the hash that is safe to persist.
	/// </summary>
	SecretToken Create();

	/// <summary>
	/// Hashes a secret received from a client so it can be compared against stored hashes.
	/// </summary>
	string Hash(string token);
}
