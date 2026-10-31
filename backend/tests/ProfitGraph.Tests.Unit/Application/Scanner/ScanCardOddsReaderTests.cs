using ProfitGraph.Application.Scanner;
using ProfitGraph.Application.Scanner.CardOdds;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Tests.Unit.Application.Scanner;

/// <summary>
///     The backtests a card's odds are pooled from (ADR 0073): the preset's own when it has any, of one preset only periods that do not
///     overlap, newest first, none overlapping a period kept apart, at most the configured count, each weighing half as much per half
///     life since its period ended.
/// </summary>
public sealed class ScanCardOddsReaderTests
{
    private static readonly DateTimeOffset Day0 = new DateTimeOffset(year: 2026, month: 10, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly ScanTradeOptions Options = new ScanTradeOptions();

    // Newest first: «Импульс» over days 1–31 and 0–30 (overlapping), over the 30 days before day 0, and «Падение» over days 0–30.
    private static readonly ScanCardOddsSourceInternal[] Candidates =
    [
        Source("Импульс", from: 1, to: 31), Source("Импульс", from: 0, to: 30), Source("Импульс", from: -30, to: 0), Source("Падение", from: 0, to: 30)
    ];

    [Test]
    public void ThePresetsOwnBacktestsOverSeparatePeriodsAreTakenNewestFirstWithWeightsByAge()
    {
        IReadOnlyList<ScanCardOddsSourceInternal> picked = ScanCardOddsReader.Pick(Candidates, "Импульс", Day0.AddDays(31), apart: null, Options);

        picked.Select(source => source.BacktestId).ShouldBe([Candidates[0].BacktestId, Candidates[2].BacktestId]);
        picked.Select(source => source.Weight).ShouldBe([1m, Math.Round((decimal)Math.Pow(x: 0.5, 31d / 30d), decimals: 6)]);
    }

    [Test]
    public void WithoutThePresetsBacktestsEveryPresetCountsAndEachKeepsItsPeriodsApart()
    {
        IReadOnlyList<ScanCardOddsSourceInternal> picked = ScanCardOddsReader.Pick(Candidates, "Другой", Day0.AddDays(31), apart: null, Options);

        picked.Select(source => source.BacktestId).ShouldBe([Candidates[0].BacktestId, Candidates[2].BacktestId, Candidates[3].BacktestId]);
        ScanCardOddsReader.Pick(Candidates, presetName: null, Day0.AddDays(31), apart: null, Options).Count.ShouldBe(3);
    }

    [Test]
    public void APeriodKeptApartAndTheSourceLimitLeaveBacktestsOut()
    {
        ScanCardOddsReader.Pick(Candidates, "Импульс", Day0.AddDays(31), (Day0.AddDays(1), Day0.AddDays(31)), Options)
            .Select(source => source.BacktestId)
            .ShouldBe([Candidates[2].BacktestId]);
        ScanCardOddsReader.Pick(Candidates, "Импульс", Day0.AddDays(31), apart: null, new ScanTradeOptions { OddsSources = 1 })
            .Select(source => source.BacktestId)
            .ShouldBe([Candidates[0].BacktestId]);
    }

    private static ScanCardOddsSourceInternal Source(string preset, int from, int to)
    {
        return new ScanCardOddsSourceInternal(Guid.NewGuid(), preset, Day0.AddDays(from), Day0.AddDays(to), Day0.AddDays(to), []);
    }
}