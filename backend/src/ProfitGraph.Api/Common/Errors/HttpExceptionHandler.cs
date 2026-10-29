using System.Diagnostics;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Scanner;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Common;
using ProfitGraph.Engine.Common;

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
            ResourceNotFoundException notFound => (404, "Resource not found", notFound.Code),
            StateConflictException conflict => (409, "State conflict", conflict.Code),
            CredentialsUnavailableException unavailable => (503, "Credentials unavailable", unavailable.Code),
            VenueFeeRatesUnavailableException fees => (503, "Venue unavailable", fees.Code),
            VenueAccountsUnavailableException accounts => (503, "Venue unavailable", accounts.Code),
            AuthenticatorCodeUnavailableException authenticator => (503, "Venue unavailable", authenticator.Code),
            BotRuleException { Code: BotRuleException.MustBeStopped or BotRuleException.TradingHalted or BotRuleException.NotRunning } bot =>
                (409, "State conflict", bot.Code),
            DomainException domain => (422, "Domain rule violation", domain.Code),
            EngineException => (422, "Domain rule violation", "domain_rule_violation"),
            InstrumentRulesException { Code: InstrumentRulesErrorCodes.InvalidRequest } instrument => (422, "Instrument unavailable", instrument.Code),
            InstrumentRulesException { Code: InstrumentRulesErrorCodes.InvalidProviderData } instrument => (502, "Invalid venue data", instrument.Code),
            InstrumentRulesException instrument => (503, "Venue unavailable", instrument.Code),
            MarketScanException { Code: MarketScanErrorCodes.InvalidProviderData } scan => (502, "Invalid venue data", scan.Code),
            MarketScanException scan => (503, "Venue unavailable", scan.Code),
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