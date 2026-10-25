using ProfitGraph.Venues.Steam.Orders;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Records the waiting codes the Steam order flows report, in order.
/// </summary>
internal sealed class RecordingSteamOrderWaiting : ISteamOrderWaiting
{
    public List<(long EngineOrderId, SteamWaitingCode? Code, DateTimeOffset? Until)> Reports { get; } = new List<(long, SteamWaitingCode?, DateTimeOffset?)>();

    public Task Report(SteamOrderJournalEntry entry, SteamWaitingCode? code, DateTimeOffset? until, CancellationToken cancellationToken)
    {
        lock (Reports)
        {
            Reports.Add((entry.EngineOrderId, code, until));
        }

        return Task.CompletedTask;
    }
}