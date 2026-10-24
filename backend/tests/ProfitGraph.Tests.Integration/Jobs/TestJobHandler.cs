using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Jobs.Ports;

namespace ProfitGraph.Tests.Integration.Jobs;

public sealed class TestJobHandler(IBackgroundJobProgress progress) : IRequestHandler<TestJobRequest, TestJobResult>
{
    public async Task<TestJobResult> Handle(TestJobRequest request, CancellationToken cancellationToken)
    {
        for (var step = 1; step <= request.Steps; step++)
        {
            await Task.Delay(request.StepMilliseconds, cancellationToken);
            await progress.Report(step, request.Steps, cancellationToken);
        }

        if (request.FailWithCode is not null)
        {
            throw new StateConflictException("The test job failed on purpose.", request.FailWithCode);
        }

        return request.ThrowUnexpected ? throw new InvalidOperationException("Unexpected test failure.") : new TestJobResult(request.Steps);
    }
}