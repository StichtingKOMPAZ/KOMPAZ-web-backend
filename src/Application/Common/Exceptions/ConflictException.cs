namespace Kompaz.Application.Common.Exceptions;

/// <summary>
/// Thrown when a request cannot be applied because it clashes with the current state, such as a duplicate email address.
/// </summary>
public class ConflictException : Exception
{
	public ConflictException(string message)
		: base(message)
	{
	}

	public ConflictException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public ConflictException()
		: base("Dit verzoek gaat niet samen met de huidige situatie.")
	{
	}
}
