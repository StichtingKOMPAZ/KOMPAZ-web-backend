namespace Kompaz.Application.Common.Exceptions;

public class ForbiddenAccessException : Exception
{
	public ForbiddenAccessException()
		: base("Access to the requested resource is forbidden.")
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
