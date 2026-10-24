using System.Diagnostics;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Common;

namespace ProfitGraph.Api.Common.Errors;

/// <summary>
///     Maps exceptions to Problem Details. Feature-specific mappings are added next to their features;
///     venue modules translate their own transport failures before they reach this handler.
/// </summary>
internal sealed class HttpExceptionHandler(IProblemDetailsService problemDetails, ILogger<HttpExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        (int status, string title, string code) = exception switch
        {
            ApplicationValidationException => (400, "Invalid request", "validation_failed"),
            ResourceNotFoundException => (404, "Resource not found", "resource_not_found"),
            StateConflictException => (409, "State conflict", "state_conflict"),
            DomainException => (422, "Domain rule violation", "domain_rule_violation"),
            BadHttpRequestException badRequest => (badRequest.StatusCode, "Invalid request", "invalid_request"),
            _ => (500, "Server error", "unexpected_error")
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status == 500 ? "An unexpected error occurred." : exception.Message
        };

        if (status == 500)
        {
            logger.LogError(exception, "Unhandled HTTP failure. TraceId={TraceId}.", Activity.Current?.Id ?? context.TraceIdentifier);
        }

        problem.Extensions["code"] = code;

        if (exception is ApplicationValidationException validation)
        {
            problem.Extensions["errors"] = validation.Errors;
        }

        HttpErrors.Enrich(problem, context);
        context.Response.StatusCode = status;
        await problemDetails.WriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problem,
                Exception = exception
            });

        return true;
    }
}