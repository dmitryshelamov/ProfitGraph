using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting;

/// <summary>
///     A run of another user is reported as missing, so its existence is not revealed (stage 3 plan, S3-4).
/// </summary>
internal static class OwnedBacktests
{
    internal static async Task<BacktestRunId> EnsureOwned(this IBacktestReader reader, Guid id, Guid userId, CancellationToken cancellationToken)
    {
        if (id != Guid.Empty)
        {
            var runId = new BacktestRunId(id);

            if (await reader.Owner(runId, cancellationToken) == new UserId(userId))
            {
                return runId;
            }
        }

        throw new ResourceNotFoundException("The backtest run does not exist.", BacktestErrorCodes.RunNotFound);
    }
}