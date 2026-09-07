namespace Kompaz.Application.Common.Behaviours;

public class ValidationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly IEnumerable<IValidator<TRequest>> _validators;

	public ValidationBehaviour(IEnumerable<IValidator<TRequest>> validators)
	{
		_validators = validators;
	}

	public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		if (!_validators.Any())
		{
			return await next();
		}

		var context = new ValidationContext<TRequest>(request);
		var results = await Task.WhenAll(_validators.Select(validator => validator.ValidateAsync(context, cancellationToken)));
		var failures = results
			.SelectMany(result => result.Errors)
			.Where(failure => failure is not null)
			.ToList();

		if (failures.Count != 0)
		{
			throw new Kompaz.Application.Common.Exceptions.ValidationException(failures);
		}

		return await next();
	}
}
