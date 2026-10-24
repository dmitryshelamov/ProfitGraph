namespace ProfitGraph.Application.Common.Exceptions;

public sealed class ApplicationValidationException(IReadOnlyCollection<InputError> errors)
    : ApplicationExceptionBase("validation_failed", "One or more input values are invalid.")
{
    public IReadOnlyCollection<InputError> Errors { get; } = errors;
}