namespace Kompaz.Application.Common.Exceptions;

/// <summary>
/// Thrown when credentials are missing, expired, already spent, or otherwise not accepted.
/// </summary>
public class AuthenticationFailedException : Exception
{
	public AuthenticationFailedException(string message)
		: base(message)
	{
	}

	public AuthenticationFailedException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public AuthenticationFailedException()
		: base("De opgegeven inloggegevens zijn niet geaccepteerd.")
	{
	}
}
