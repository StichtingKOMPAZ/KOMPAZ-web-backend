namespace Kompaz.Application.Common.Exceptions;

public class ForbiddenAccessException : Exception
{
	public ForbiddenAccessException()
		: base("Toegang tot het opgevraagde onderdeel is niet toegestaan.")
	{
	}

	public ForbiddenAccessException(string message)
		: base(message)
	{
	}

	public ForbiddenAccessException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
