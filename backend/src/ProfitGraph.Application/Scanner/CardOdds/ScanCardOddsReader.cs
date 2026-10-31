using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.CardOdds;

/// <summary>
///     Picks the backtests a card's odds are pooled from (ADR 0073): among the owner's latest completed backtests of the venue with
///     cards, those of the preset the pair was found by when there are any, otherwise all; of one preset only backtests over periods
///     that do not overlap, newest first, so no signal counts twice; at most <see cref="ScanTradeOptions.OddsSources" />. Each weighs
///     half as much per <see cref="ScanTradeOptions.OddsHalfLife" /> between its period's end and the time the odds are for.
/// </summary>
public sealed class ScanCardOddsReader(IScanBacktestReader backtests, ScanTradeOptions options)
{
    private const int Decimals = 6;

    /// <summary>
    ///     How many more backtests than the sources are read to choose from.
    /// </summary>
    private const int Candidates = 4;

    /// <summary>
    ///     The odds for <paramref name="at" />, leaving out backtests whose period overlaps <paramref name="apart" />; null without a
    ///     source.
    /// </summary>
    public async Task<ScanCardOddsBasisInternal?> Read(
        Guid ownerId,
        string venue,
        string? presetName,
        DateTimeOffset at,
        (DateTimeOffset From, DateTimeOffset To)? apart,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ScanCardOddsSourceInternal> candidates = await backtests.CardOddsSources(ownerId, venue, options.OddsSources * Candidates, cancellationToken);
        IReadOnlyList<ScanCardOddsSourceInternal> sources = Pick(candidates, presetName, at, apart, options);

        return sources.Count == 0
            ? null
            : new ScanCardOddsBasisInternal(
                sources,
                new ScanCardOddsModel(
                    sources.Select(source => new ScanWeightedOdds(source.Odds, source.Weight)),
                    options.OddsPriorTrades,
                    options.MinOddsTrades));
    }

    /// <summary>
    ///     The sources among <paramref name="candidates" />, newest first, with their weights.
    /// </summary>
    public static IReadOnlyList<ScanCardOddsSourceInternal> Pick(
        IReadOnlyList<ScanCardOddsSourceInternal> candidates,
        string? presetName,
        DateTimeOffset at,
        (DateTimeOffset From, DateTimeOffset To)? apart,
        ScanTradeOptions options)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);

        ScanCardOddsSourceInternal[] usable =
        [
            .. candidates.Where(candidate => apart is not { } period || !Overlap(candidate.From, candidate.To, period.From, period.To))
        ];
        ScanCardOddsSourceInternal[] preset = [.. usable.Where(candidate => presetName is not null && candidate.PresetName == presetName)];
        List<ScanCardOddsSourceInternal> picked = [];

        foreach (ScanCardOddsSourceInternal candidate in preset.Length > 0 ? preset : usable)
        {
            if (picked.Count == options.OddsSources)
            {
                break;
            }

            if (picked.Any(source => source.PresetName == candidate.PresetName && Overlap(source.From, source.To, candidate.From, candidate.To)))
            {
                continue;
            }

            picked.Add(candidate);
        }

        return [.. picked.Select(source => source with { Weight = Weight(source.To, at, options.OddsHalfLife) })];
    }

    private static decimal Weight(DateTimeOffset end, DateTimeOffset at, TimeSpan halfLife)
    {
        double halves = Math.Abs((at - end).Ticks) / (double)halfLife.Ticks;

        return Math.Max(Math.Round((decimal)Math.Pow(x: 0.5, halves), Decimals), val2: 0.000001m);
    }

    private static bool Overlap(DateTimeOffset from, DateTimeOffset to, DateTimeOffset otherFrom, DateTimeOffset otherTo)
    {
        return from < otherTo && otherFrom < to;
    }
}