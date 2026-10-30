using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Domain.Scanner.AutoTrades;

namespace ProfitGraph.Application.Scanner.AutoTrades;

/// <summary>
///     Checks a new snapshot against every auto trade rule of its venue that is on (ADR 0071, section 6) and stores a request for each
///     pair that entered the rule's preset, once per pair and rule an hour, as alerts are checked. The scanner does nothing else: it
///     reads no fees, starts no bots and calls no venue. A row whose candles are still being repaired counts as outside every result.
/// </summary>
public sealed class ScanAutoTradeQueue(
    IScanAutoTradeRuleRepository rules,
    IScanPresetRepository presets,
    IScanAutoTradeRequestRepository requests,
    TimeProvider clock)
{
    public async Task Check(ScanSnapshotInternal snapshot, CancellationToken cancellationToken)
    {
        foreach (ScanAutoTradeRule rule in await rules.ListOnForUpdate(new VenueId(snapshot.Venue), cancellationToken))
        {
            if (await presets.GetForUpdate(rule.PresetId, rule.OwnerId, cancellationToken) is not { } preset)
            {
                continue;
            }

            Dictionary<string, ScanSnapshotRowInternal> kept = snapshot.Rows
                .Where(row => !row.Incomplete && preset.Keeps(row.Metrics))
                .ToDictionary(row => row.Symbol, StringComparer.Ordinal);
            IReadOnlyList<string> entered = rule.Check(kept.Keys, snapshot.ComputedAt);
            await rules.Update(rule, cancellationToken);

            foreach (string symbol in entered)
            {
                await requests.Add(
                    ScanAutoTradeRequest.Create(rule.Id, symbol, snapshot.ComputedAt, kept[symbol].Metrics.Price, clock.GetUtcNow()),
                    cancellationToken);
            }
        }
    }
}