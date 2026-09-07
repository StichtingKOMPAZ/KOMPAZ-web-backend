using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Models;
using System.Security.Cryptography;
using System.Text;

namespace Kompaz.Infrastructure.Authentication;

/// <summary>
/// Generates opaque secrets from a cryptographic random source and hashes them for storage.
/// The hash is unsalted SHA-256 on purpose: the input is 256 bits of entropy, so it is not guessable.
/// </summary>
internal sealed class SecretTokenFactory : ISecretTokenFactory
{
	private const int TokenBytes = 32;

	public SecretToken Create()
	{
		string token = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

		return new SecretToken(token, Hash(token));
	}

	public string Hash(string token) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

	private static string Base64UrlEncode(byte[] value) =>
		Convert.ToBase64String(value)
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');
}
