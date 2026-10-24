using System.Diagnostics;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Api.Errors;

internal static class HttpErrors
{
    internal static void Enrich(ProblemDetails problem, HttpContext context)
    {
        problem.Instance ??= context.Request.Path;
        problem.Extensions.TryAdd("code", $"http_{problem.Status ?? context.Response.StatusCode}");
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
    }

    internal static IActionResult InvalidModel(ActionContext context)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid request",
            Detail = "One or more input values are invalid."
        };
        problem.Extensions["code"] = "validation_failed";
        problem.Extensions["errors"] = context.ModelState
            .SelectMany(entry => entry.Value!.Errors.Select(_ => new InputError("invalid_input", entry.Key, "The supplied value is invalid.")))
            .ToArray();
        Enrich(problem, context.HttpContext);
        var response = new ObjectResult(problem) { StatusCode = problem.Status };
        response.ContentTypes.Add("application/problem+json");

        return response;
    }
}