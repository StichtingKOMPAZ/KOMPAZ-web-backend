using FluentValidation.Results;

namespace Kompaz.Application.Common.Exceptions;

public class ValidationException : Exception
{
	public ValidationException(IEnumerable<ValidationFailure> failures)
		: base("One or more validation failures have occurred.")
	{
		Errors = failures
			.GroupBy(failure => failure.PropertyName, failure => failure.ErrorMessage)
			.ToDictionary(group => group.Key, group => group.Distinct().ToArray());
	}

	public IDictionary<string, string[]> Errors { get; }
}
