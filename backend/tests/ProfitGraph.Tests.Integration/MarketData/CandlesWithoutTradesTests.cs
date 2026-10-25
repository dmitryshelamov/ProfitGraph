using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Backtesting.History;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.MarketData;

/// <summary>
///     Candles of an open market without trades (ADR 0023, TC2) are cached with their flag, kept by prepared history and
///     never replaced by a CSV file.
/// </summary>
[NonParallelizable]
public sealed class CandlesWithoutTradesTests
{
    private static readonly DateTimeOffset First = FakeHistorySource.DefaultFirst;
    private static readonly DateTimeOffset ObservedAt = First.AddDays(1);
    private static readonly DateTimeOffset Quiet = First.AddHours(2);
    private ProfitGraphApiFactory _application = null!;
    private FakeHistorySource _history = null!;
    private string _symbol = string.Empty;

    [OneTimeSetUp]
    public void StartApi()
    {
        _application = new ProfitGraphApiFactory();
        _history = _application.Services.GetRequiredService<FakeHistorySource>();
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _application.DisposeAsync();
    }

    [SetUp]
    public void ReportAQuietHour()
    {
        _history.Reset();
        _history.Candles = (timeframe, from, to) =>
        [
            .. FakeHistorySource.Complete(timeframe, from, to)
                .Select(candle => candle.OpenTime == Quiet ? candle with { Open = 105, High = 105, Low = 105, Volume = 0, NoTrades = true } : candle)
        ];
        _symbol = "Q" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }

    [Test]
    public async Task PreparedHistoryKeepsTheFlag_AndItChangesTheIdentity()
    {
        PreparedHistory quiet = await Prepare();
        _history.Candles = (timeframe, from, to) =>
        [
            .. FakeHistorySource.Complete(timeframe, from, to)
                .Select(candle => candle.OpenTime == Quiet ? candle with { Open = 105, High = 105, Low = 105, Volume = 0 } : candle)
        ];
        _symbol = "Q" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        PreparedHistory traded = await Prepare();

        quiet.Candles.Single(candle => candle.OpenTime == Quiet).NoTrades.ShouldBeTrue();
        quiet.Candles.Count(candle => candle.NoTrades).ShouldBe(1);
        traded.Candles.ShouldAllBe(candle => !candle.NoTrades);
        quiet.Identity.ShouldNotBe(traded.Identity);
    }

    [Test]
    public async Task ACsvCandleReplacingACandleWithoutTrades_IsRejected()
    {
        await Prepare();
        using HttpClient client = await _application.CreateSignedInClient(UserRole.Owner);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(TestHistoryVenue.WithHistory), "Venue");
        form.Add(new StringContent(_symbol), "Symbol");
        form.Add(new StringContent("1h"), "Timeframe");
        form.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes($"openTime,open,high,low,close,volume\n{Quiet:yyyy-MM-ddTHH:mm:ssZ},100,110,90,105,1\n")),
            "File",
            "candles.csv");

        using HttpResponseMessage response = await client.PostAsync("/api/historical-data/csv", form);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("without trades");
        (await Prepare()).Candles.Single(candle => candle.OpenTime == Quiet).NoTrades.ShouldBeTrue();
    }

    private async Task<PreparedHistory> Prepare()
    {
        using IServiceScope scope = _application.Services.CreateScope();
        CandleSeries series = scope.ServiceProvider.GetRequiredService<VenueRegistry>().ResolveSeries(TestHistoryVenue.WithHistory, _symbol, "1h");

        return await scope.ServiceProvider.GetRequiredService<HistoryPreparationService>()
            .Prepare(series, First.AddHours(1), First.AddHours(4), warmupCandles: 1, ObservedAt, reportProgress: false, CancellationToken.None);
    }
}