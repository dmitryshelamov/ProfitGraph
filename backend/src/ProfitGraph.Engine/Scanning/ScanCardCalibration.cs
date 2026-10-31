namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     How well the odds of other backtests predicted a backtest's cards (ADR 0073): the source backtests, how many closed cards got a
///     chance, the share that reached the target first and the mean predicted chance, and the Brier score (the mean squared gap between
///     the chance and the outcome, lower is better) of the chances, of the random walk's <c>1 / (1 + RR)</c> alone and of the bands'
///     measured shares before ADR 0073 (the random walk where a band had too few trades), and the cards by tenths of the predicted chance.
/// </summary>
public sealed record ScanCardCalibration(
    IReadOnlyList<Guid> Sources,
    int Cards,
    decimal Observed,
    decimal Predicted,
    decimal Brier,
    decimal GeometryBrier,
    decimal BandsBrier,
    IReadOnlyList<ScanCalibrationBin> Bins)
{
    private const int Decimals = 6;
    private const int BinCount = 10;

    /// <summary>
    ///     The calibration of <paramref name="model" />, built from the odds of <paramref name="sources" />, on the closed
    ///     <paramref name="cards" /> it gives a chance; null without one.
    /// </summary>
    public static ScanCardCalibration? Of(IReadOnlyList<Guid> sources, ScanCardOddsModel model, IEnumerable<ScanCardTrade> cards)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(cards);

        (decimal Chance, decimal Geometry, decimal Banded, decimal Hit)[] scored =
        [
            .. cards
                .Where(card => card.Outcome is { Filled: true, Exit: not null })
                .Select(card => (Card: card, Odds: model.Of(card)))
                .Where(item => item.Odds is not null)
                .Select(item => (
                    item.Odds!.Chance,
                    item.Card.Geometry,
                    model.Banded(item.Card) ?? item.Card.Geometry,
                    item.Card.Outcome.Exit == ScanTradeExit.Target ? 1m : 0m))
        ];

        if (scored.Length == 0)
        {
            return null;
        }

        return new ScanCardCalibration(
            sources,
            scored.Length,
            Mean(scored.Select(item => item.Hit)),
            Mean(scored.Select(item => item.Chance)),
            Mean(scored.Select(item => (item.Chance - item.Hit) * (item.Chance - item.Hit))),
            Mean(scored.Select(item => (item.Geometry - item.Hit) * (item.Geometry - item.Hit))),
            Mean(scored.Select(item => (item.Banded - item.Hit) * (item.Banded - item.Hit))),
            [
                .. scored
                    .GroupBy(item => Math.Min((int)(item.Chance * BinCount), BinCount - 1))
                    .OrderBy(bin => bin.Key)
                    .Select(bin => new ScanCalibrationBin(
                        (decimal)bin.Key / BinCount,
                        (decimal)(bin.Key + 1) / BinCount,
                        bin.Count(),
                        Mean(bin.Select(item => item.Chance)),
                        Mean(bin.Select(item => item.Hit))))
            ]);
    }

    private static decimal Mean(IEnumerable<decimal> values)
    {
        decimal[] all = [.. values];

        return Math.Round(all.Sum() / all.Length, Decimals);
    }
}