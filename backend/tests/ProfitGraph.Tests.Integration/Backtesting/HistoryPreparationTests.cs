using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Backtesting.History;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Backtesting;

/// <summary>
///     History of one run: warm-up, an immutable copy and its identity (Profit Fox Designer ADR 0021).
/// </summary>
[NonParallelizable]
public sealed class HistoryPreparationTests
{
    private static readonly DateTimeOffset First = FakeHistorySource.DefaultFirst;
    private static readonly DateTimeOffset ObservedAt = First.AddDays(1);
    private ProfitGraphApiFactory _application = null!;
    private FakeHistorySource _history = null!;
    private string _symbol = string.Empty;

    [OneTimeSetUp]
    public void StartApi()
    {
        _application = new ProfitGraphApiFactory(new Dictionary<string, string> { ["HistoricalDownload:MaxCandlesPerChunk"] = "4" });
        _history = _application.Services.GetRequiredService<FakeHistorySource>();
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _application.DisposeAsync();
    }

    [SetUp]
    public void ResetVenue()
    {
        _history.Reset();
        _symbol = "P" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }

    [Test]
    public async Task WarmupIsClippedToTheVenuesFirstCandle()
    {
        PreparedHistory prepared = await Prepare(First.AddHours(3), First.AddHours(8), warmup: 5);

        prepared.ShouldSatisfyAllConditions(
            () => prepared.Series.Venue.Value.ShouldBe(TestHistoryVenue.WithHistory),
            () => prepared.Series.Symbol.Value.ShouldBe(_symbol),
            () => prepared.ObservedAt.ShouldBe(ObservedAt),
            () => prepared.HistoryFrom.ShouldBe(First),
            () => prepared.From.ShouldBe(First.AddHours(3)),
            () => prepared.To.ShouldBe(First.AddHours(8)),
            () => prepared.WarmupCount.ShouldBe(3),
            () => prepared.LatestClosed.ShouldBe(ObservedAt),
            () => prepared.Candles.Select(candle => candle.OpenTime).ShouldBe(Enumerable.Range(start: 0, count: 8).Select(hour => First.AddHours(hour))));
        PreparedReplay replay = prepared.ToReplay();
        replay.Warmup.Count.ShouldBe(3);
        replay.Replay.Count.ShouldBe(5);
    }

    [Test]
    public async Task FullWarmupComesBeforeTheReplayRange()
    {
        PreparedHistory prepared = await Prepare(First.AddHours(6), First.AddHours(8), warmup: 2);

        prepared.HistoryFrom.ShouldBe(First.AddHours(4));
        prepared.WarmupCount.ShouldBe(2);
        prepared.Candles.Count.ShouldBe(4);
    }

    [Test]
    public async Task VenueWithoutHistoryBeforeTheStart_HasNoWarmup()
    {
        PreparedHistory prepared = await Prepare(First, First.AddHours(2), warmup: 10);

        prepared.HistoryFrom.ShouldBe(First);
        prepared.WarmupCount.ShouldBe(0);
    }

    [Test]
    public async Task EqualInputGivesEqualIdentity_AndAnyCandleChangesIt()
    {
        PreparedHistory first = await Prepare(First.AddHours(2), First.AddHours(6), warmup: 2);
        PreparedHistory second = await Prepare(First.AddHours(2), First.AddHours(6), warmup: 2, ObservedAt.AddMinutes(30));
        await Import(TestHistoryVenue.WithHistory, [new Candle(First.AddHours(4), Open: 100, High: 110, Low: 90, Close: 106, Volume: 1)]);
        PreparedHistory changed = await Prepare(First.AddHours(2), First.AddHours(6), warmup: 2);

        first.Identity.ShouldMatch("^[0-9a-f]{64}$");
        second.Identity.ShouldBe(first.Identity);
        changed.Identity.ShouldNotBe(first.Identity);
        first.Candles[2].Close.ShouldBe(105);
    }

    [Test]
    public async Task ADifferentPartitionOfTheSameCandlesHasAnotherIdentity()
    {
        PreparedHistory oneWarmup = await Prepare(First.AddHours(2), First.AddHours(6), warmup: 1);
        PreparedHistory twoWarmup = await Prepare(First.AddHours(3), First.AddHours(6), warmup: 2);

        twoWarmup.Candles.ShouldBe(oneWarmup.Candles);
        twoWarmup.Identity.ShouldNotBe(oneWarmup.Identity);
    }

    [Test]
    public async Task CachedHistoryNeedsNoVenue()
    {
        Candle[] candles = [.. FakeHistorySource.Complete(Timeframe.Parse("1h"), First, First.AddHours(6))];
        await Import(TestHistoryVenue.WithoutHistory, candles);

        PreparedHistory prepared = await Prepare(First.AddHours(2), First.AddHours(6), warmup: 2, venue: TestHistoryVenue.WithoutHistory);

        prepared.HistoryFrom.ShouldBe(First);
        prepared.Candles.ShouldBe(candles);
    }

    [Test]
    public async Task MissingWarmupOnAVenueWithoutHistory_IsUnavailable()
    {
        await Import(TestHistoryVenue.WithoutHistory, [.. FakeHistorySource.Complete(Timeframe.Parse("1h"), First.AddHours(2), First.AddHours(6))]);

        var error = await Should.ThrowAsync<HistoricalDataException>(() =>
            Prepare(First.AddHours(2), First.AddHours(6), warmup: 2, venue: TestHistoryVenue.WithoutHistory));

        error.Code.ShouldBe(HistoricalDataErrorCodes.Unavailable);
    }

    [Test]
    public async Task IncompleteVenueHistory_FailsPreparation()
    {
        _history.Candles = (timeframe, from, to) => [.. FakeHistorySource.Complete(timeframe, from, to).Where(candle => candle.OpenTime != First.AddHours(3))];

        var error = await Should.ThrowAsync<HistoricalDataException>(() => Prepare(First.AddHours(2), First.AddHours(6), warmup: 1));

        error.Code.ShouldBe(HistoricalDataErrorCodes.Unavailable);
    }

    [TestCase(-1)]
    [TestCase(HistoryPreparationService.MaxWarmupCandles + 1)]
    public async Task WarmupOutOfRange_IsAValidationError(int warmup)
    {
        await Should.ThrowAsync<ApplicationValidationException>(() => Prepare(First.AddHours(2), First.AddHours(6), warmup));
    }

    private async Task<PreparedHistory> Prepare(
        DateTimeOffset from,
        DateTimeOffset to,
        int warmup,
        DateTimeOffset? observedAt = null,
        string venue = TestHistoryVenue.WithHistory)
    {
        using IServiceScope scope = _application.Services.CreateScope();
        CandleSeries series = scope.ServiceProvider.GetRequiredService<VenueRegistry>().ResolveSeries(venue, _symbol, "1h");

        return await scope.ServiceProvider.GetRequiredService<HistoryPreparationService>()
            .Prepare(series, from, to, warmup, observedAt ?? ObservedAt, reportProgress: false, CancellationToken.None);
    }

    private async Task Import(string venue, IReadOnlyList<Candle> candles)
    {
        using IServiceScope scope = _application.Services.CreateScope();
        CandleSeries series = scope.ServiceProvider.GetRequiredService<VenueRegistry>().ResolveSeries(venue, _symbol, "1h");
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<ICandleRepository>().Import(series, candles, ObservedAt, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }
}