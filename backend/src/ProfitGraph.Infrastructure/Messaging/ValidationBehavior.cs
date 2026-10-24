using FluentValidation;
using FluentValidation.Results;

using MediatR;

using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Infrastructure.Messaging;

internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<InputError>();

        // Sequential execution also supports validators sharing scoped dependencies.
        foreach (IValidator<TRequest> validator in validators)
        {
            ValidationResult result = await validator.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken);
            errors.AddRange(result.Errors.Select(error => new InputError(error.ErrorCode, error.PropertyName, error.ErrorMessage)));
        }

        if (errors.Count > 0)
        {
            throw new ApplicationValidationException(errors);
        }

        return await next(cancellationToken);
    }
}