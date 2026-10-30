using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Domain.Scanner.AutoTrades;

namespace ProfitGraph.Application.Scanner.AutoTrades;

/// <summary>
///     Finds what proves a rule (ADR 0071, section 5): among the owner's completed backtests of the venue whose conditions equal the
///     preset's, the latest one, and the latest one over another period in which the venue's reference pair moved the other way. For
///     each it lists the kinds of cards with their trades and mean R per third among the cards whose expected R and chance reach the
///     minimums.
/// </summary>
public sealed class ScanAutoTradeProofReader(IScanBacktestReader backtests, ScanAutoTradeOptions options)
{
    private static readonly IReadOnlyList<ScanAutoTradeThird> Empty =
        [new ScanAutoTradeThird(Trades: 0, MeanR: null), new ScanAutoTradeThird(Trades: 0, MeanR: null), new ScanAutoTradeThird(Trades: 0, MeanR: null)];

    public async Task<ScanAutoTradeEvidenceInternal> Read(
        Guid ownerId,
        ScanPreset preset,
        decimal minExpectedR,
        decimal? minChance,
        CancellationToken cancellationToken)
    {
        ScanBacktestEvidenceInternal[] matching =
        [
            .. (await backtests.Evidence(ownerId, preset.Venue.Value, options.EvidenceLimit, cancellationToken)).Where(backtest => Same(backtest.Filter, preset.Filter))
        ];
        ScanBacktestEvidenceInternal? latest = matching.FirstOrDefault();
        ScanBacktestEvidenceInternal? other = latest?.MarketChange is { } change
            ? matching.FirstOrDefault(backtest => backtest.MarketChange is { } moved && Math.Sign(moved) * Math.Sign(change) < 0 && backtest.To <= latest.From)
            : null;

        return new ScanAutoTradeEvidenceInternal(
            latest,
            latest is null ? [] : await Candidates(latest.BacktestId, minExpectedR, minChance, cancellationToken),
            other,
            other is null ? [] : await Candidates(other.BacktestId, minExpectedR, minChance, cancellationToken));
    }

    /// <summary>
    ///     The proof of the rule's card from the evidence, or null when a backtest is missing.
    /// </summary>
    public static ScanAutoTradeProof? Proof(ScanAutoTradeEvidenceInternal evidence, ScanAutoTradeCard card)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(card);

        if (evidence is not { Latest: { } latest, OtherRegime: { } other })
        {
            return null;
        }

        return new ScanAutoTradeProof(Evidence(latest, evidence.LatestCandidates, card), Evidence(other, evidence.OtherCandidates, card));
    }

    private static ScanAutoTradeEvidence Evidence(ScanBacktestEvidenceInternal backtest, IReadOnlyList<ScanAutoTradeCandidateInternal> candidates, ScanAutoTradeCard card)
    {
        ScanAutoTradeCandidateInternal? kind = candidates.FirstOrDefault(candidate =>
            candidate.Timeframe == card.Timeframe && candidate.Scenario == card.Scenario && candidate.Direction == card.Direction);

        return new ScanAutoTradeEvidence(new ScanBacktestId(backtest.BacktestId), kind?.Thirds ?? Empty, backtest.MarketChange);
    }

    private async Task<IReadOnlyList<ScanAutoTradeCandidateInternal>> Candidates(
        Guid backtestId,
        decimal minExpectedR,
        decimal? minChance,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ScanCardGroupInternal> groups = await backtests.CardGroups(backtestId, minExpectedR, minChance, cancellationToken);

        return
        [
            .. groups
                .GroupBy(group => (group.Timeframe, group.Scenario, group.Direction))
                .Select(kind =>
                {
                    ScanAutoTradeThird[] thirds =
                    [
                        .. Enumerable.Range(start: 0, count: 3)
                            .Select(third => kind.FirstOrDefault(group => group.Third == third) is { } found
                                ? new ScanAutoTradeThird(found.Trades, found.MeanR)
                                : new ScanAutoTradeThird(Trades: 0, MeanR: null))
                    ];
                    var evidence = new ScanAutoTradeEvidence(new ScanBacktestId(backtestId), thirds, MarketChange: null);

                    return new ScanAutoTradeCandidateInternal(kind.Key.Timeframe, kind.Key.Scenario, kind.Key.Direction, thirds, evidence.Proves(options.Thresholds));
                })
        ];
    }

    /// <summary>
    ///     Whether two filters keep the same pairs: the same conditions in any order and the same choice on stale pairs.
    /// </summary>
    private static bool Same(ScanFilter left, ScanFilter right)
    {
        return left.HideStale == right.HideStale && left.Conditions.Count == right.Conditions.Count &&
            left.Conditions.ToHashSet().SetEquals(right.Conditions);
    }
}