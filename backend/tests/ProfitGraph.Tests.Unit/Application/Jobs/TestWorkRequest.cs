using ProfitGraph.Application.Jobs;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Tests.Unit.Application.Jobs;

public sealed record TestWorkRequest(int Size) : IBackgroundJobRequest<TestWorkResult>
{
    public static BackgroundJobKind Kind { get; } = new BackgroundJobKind("test_work");
}