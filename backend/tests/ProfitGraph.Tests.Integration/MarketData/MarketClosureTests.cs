using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Backtesting.History;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.MarketData;

/// <summary>
///     A venue whose market closes at night (ADR 0023, TC1): open 04:00–20:00 UTC, closed otherwise. Closures are cached
///     beside the candles, preparation accepts the gaps they cover and warm-up counts candles across them.
/// </summary>
[NonParallelizable]
public sealed class MarketClosureTests
{
    private static readonly DateTimeOffset First = FakeHistorySource.DefaultFirst;
    private static readonly DateTimeOffset DayOne = First.AddDays(1);
    private static readonly DateTimeOffset DayTwo = First.AddDays(2);
    private static readonly DateTimeOffset ObservedAt = First.AddDays(3);
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
    public void OpenTheMarketByDay()
    {
        _history.Reset();
        _history.Candles = (timeframe, from, to) => [.. FakeHistorySource.Complete(timeframe, from, to).Where(candle => IsOpen(candle.OpenTime))];
        _history.Closures = (timeframe, from, to) => Closed(timeframe, from, to);
        _symbol = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }

    [Test]
    public async Task HistoryAcrossTheNight_HasItsClosureMergedOverChunks()
    {
        PreparedHistory prepared = await Prepare(DayOne.AddHours(4), DayTwo.AddHours(20), warmup: 0);

        prepared.Candles.Count.ShouldBe(32);
        prepared.Closures.ShouldBe([new MarketClosure(DayOne.AddHours(20), DayTwo.AddHours(4))]);
        PreparedReplay replay = prepared.ToReplay();
        replay.Replay.Count.ShouldBe(32);
        replay.Closures.ShouldBe(prepared.Closures);
    }

    [Test]
    public async Task WarmupCountsCandles_AndReachesBackOverTheNight()
    {
        PreparedHistory prepared = await Prepare(DayTwo.AddHours(4), DayTwo.AddHours(8), warmup: 6);

        prepared.ShouldSatisfyAllConditions(
            () => prepared.WarmupCount.ShouldBe(6),
            () => prepared.HistoryFrom.ShouldBe(DayOne.AddHours(14)),
            () => prepared.Candles.Take(6).Select(candle => candle.OpenTime).ShouldBe(Enumerable.Range(start: 14, count: 6).Select(hour => DayOne.AddHours(hour))),
            () => prepared.Closures.ShouldBe([new MarketClosure(DayOne.AddHours(20), DayTwo.AddHours(4))]));
        prepared.ToReplay().Warmup.Count.ShouldBe(6);
    }

    [Test]
    public async Task CachedClosures_NeedNoVenueRequest_AndGiveTheSameIdentity()
    {
        PreparedHistory first = await Prepare(DayTwo.AddHours(4), DayTwo.AddHours(8), warmup: 6);

        lock (_history.Requests)
        {
            _history.Requests.Clear();
        }

        PreparedHistory second = await Prepare(DayTwo.AddHours(4), DayTwo.AddHours(8), warmup: 6);

        _history.Requests.ShouldBeEmpty();
        second.Identity.ShouldBe(first.Identity);
    }

    [Test]
    public async Task ClosuresChangeTheIdentity()
    {
        PreparedHistory closed = await Prepare(DayOne.AddHours(18), DayTwo.AddHours(6), warmup: 0);
        _symbol = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        _history.Candles = null;
        _history.Closures = null;
        PreparedHistory open = await Prepare(DayOne.AddHours(18), DayTwo.AddHours(6), warmup: 0);

        closed.Closures.ShouldNotBeEmpty();
        open.Closures.ShouldBeEmpty();
        closed.Identity.ShouldNotBe(open.Identity);
    }

    [Test]
    public async Task AGapNoClosureCovers_StillFails()
    {
        _history.Closures = (timeframe, from, to) => [.. Closed(timeframe, from, to).Where(closure => closure.From != DayOne.AddHours(20))];

        var error = await Should.ThrowAsync<HistoricalDataException>(() => Prepare(DayOne.AddHours(18), DayTwo.AddHours(6), warmup: 0));

        error.Code.ShouldBe(HistoricalDataErrorCodes.Unavailable);
    }

    [Test]
    public async Task ACandleInsideAReportedClosure_IsInvalidProviderData()
    {
        _history.Closures = (_, from, to) => [new CandleRangeInternal(from, to)];

        var error = await Should.ThrowAsync<HistoricalDataException>(() => Prepare(DayOne.AddHours(4), DayOne.AddHours(8), warmup: 0));

        error.Code.ShouldBe(HistoricalDataErrorCodes.InvalidProviderData);
    }

    [Test]
    public async Task ACsvCandleInsideAStoredClosure_IsRejected()
    {
        await Prepare(DayOne.AddHours(18), DayTwo.AddHours(6), warmup: 0);
        using HttpClient client = await _application.CreateSignedInClient(UserRole.Owner);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(TestHistoryVenue.WithHistory), "Venue");
        form.Add(new StringContent(_symbol), "Symbol");
        form.Add(new StringContent("1h"), "Timeframe");
        form.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes($"openTime,open,high,low,close,volume\n{DayOne.AddHours(22):yyyy-MM-ddTHH:mm:ssZ},100,110,90,105,1\n")),
            "File",
            "candles.csv");

        using HttpResponseMessage response = await client.PostAsync("/api/historical-data/csv", form);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("market closure");
    }

    private static bool IsOpen(DateTimeOffset time)
    {
        return time.Hour is >= 4 and < 20;
    }

    private static List<CandleRangeInternal> Closed(Timeframe timeframe, DateTimeOffset from, DateTimeOffset to)
    {
        var closures = new List<CandleRangeInternal>();

        for (DateTimeOffset time = from; time < to; time += timeframe.Duration)
        {
            if (IsOpen(time))
            {
                continue;
            }

            if (closures.Count > 0 && closures[^1].To == time)
            {
                closures[^1] = closures[^1] with { To = time + timeframe.Duration };
            }
            else
            {
                closures.Add(new CandleRangeInternal(time, time + timeframe.Duration));
            }
        }

        return closures;
    }

    private async Task<PreparedHistory> Prepare(DateTimeOffset from, DateTimeOffset to, int warmup)
    {
        using IServiceScope scope = _application.Services.CreateScope();
        CandleSeries series = scope.ServiceProvider.GetRequiredService<VenueRegistry>().ResolveSeries(TestHistoryVenue.WithHistory, _symbol, "1h");

        return await scope.ServiceProvider.GetRequiredService<HistoryPreparationService>()
            .Prepare(series, from, to, warmup, ObservedAt, reportProgress: false, CancellationToken.None);
    }
}