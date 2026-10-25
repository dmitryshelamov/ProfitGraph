using ProfitGraph.Domain.Bots;
using ProfitGraph.Venues.TInvest.Trading;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     Records what the module reports to the bot runtime about waiting orders and answers whether the bot may send.
/// </summary>
internal sealed class RecordingTInvestOrderWaits : ITInvestOrderWaits
{
    public List<(long EngineOrderId, OrderWaitingCode? Code, decimal? Low, decimal? High)> Reports { get; } = [];

    public bool MaySend { get; set; } = true;

    public Task Report(BotId botId, long engineOrderId, OrderWaitingCode? code, decimal? low, decimal? high, CancellationToken cancellationToken)
    {
        lock (Reports)
        {
            Reports.Add((engineOrderId, code, low, high));
        }

        return Task.CompletedTask;
    }

    public Task<bool> MayRelease(BotId botId, CancellationToken cancellationToken)
    {
        return Task.FromResult(MaySend);
    }
}