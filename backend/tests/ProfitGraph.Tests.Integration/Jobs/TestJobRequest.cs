using ProfitGraph.Application.Jobs;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Tests.Integration.Jobs;

/// <summary>
///     Work for job tests: reports progress after each step and can fail with an application code or unexpectedly.
/// </summary>
public sealed record TestJobRequest(int Steps, int StepMilliseconds, string? FailWithCode = null, bool ThrowUnexpected = false)
    : IBackgroundJobRequest<TestJobResult>
{
    public static BackgroundJobKind Kind { get; } = new BackgroundJobKind("test_job");
}