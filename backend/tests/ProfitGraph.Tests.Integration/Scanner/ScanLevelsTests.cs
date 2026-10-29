using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.MarketData.Charts;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Application.Scanner.ReplaceScanSnapshot;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     A scanner pair's levels on the chart's timeframe over HTTP (ADR 0059, section 8): each timeframe draws its own levels from its
///     last closed candles and applies them at the price of the pair's snapshot row, or at the last close without one. Values are
///     invented; no venue is contacted.
/// </summary>
[NonParallelizable]
public sealed class ScanLevelsTests
{
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private ProfitGraphApiFactory _api = null!;
    private FakeHistorySource _history = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory(services: services => services.AddSingleton<IMarketScanSource, TestScanSource>());
        _history = _api.Services.GetRequiredService<FakeHistorySource>();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [SetUp]
    public void ShapeVenue()
    {
        _history.Reset();
        _history.Candles = Shaped;
    }

    [TearDown]
    public async Task ClearSnapshot()
    {
        await Replace();
    }

    [Test]
    public async Task EachTimeframeHasItsOwnLevelsAtTheSnapshotPrice()
    {
        await Replace(new ScanSnapshotRowInternal("LEVELA", new ScanMetrics { Price = 100.5m }, Incomplete: false));
        using HttpClient client = await _api.CreateSignedInClient();

        ScanLevelsResponse hourly = await Levels(client, "LEVELA", "1h");
        ScanLevelsResponse daily = await Levels(client, "LEVELA", "1d");

        (hourly.Timeframe, hourly.Status, hourly.CandleCount, hourly.Price).ShouldBe(("1h", CandleHistoryStatus.Complete, ScanLevels.MaximumCandles, "100.5"));
        hourly.Levels.ShouldBe(["90", "110"]);
        (hourly.Support, hourly.Resistance).ShouldBe(("90", "110"));
        Rounded(hourly.RewardRisk).ShouldBe(0.9048m);
        Rounded(hourly.DistanceToResistance).ShouldBe(9.4527m);
        Rounded(hourly.DistanceToSupport).ShouldBe(-10.4478m);
        daily.Levels.ShouldBe(["95", "120"]);
        (daily.Support, daily.Resistance).ShouldBe(("95", "120"));
        Rounded(daily.RewardRisk).ShouldBe(3.5455m);
    }

    [Test]
    public async Task APairOutsideTheSnapshotUsesTheLastClose()
    {
        using HttpClient client = await _api.CreateSignedInClient();

        ScanLevelsResponse levels = await Levels(client, "LEVELB", "1h");

        (levels.Price, levels.Support, levels.Resistance).ShouldBe(("100.5", "90", "110"));
    }

    [Test]
    public async Task RefusesAVenueWithoutAScannerAndAnUnknownTimeframe()
    {
        using HttpClient client = await _api.CreateSignedInClient();

        HttpResponseMessage noScanner = await client.GetAsync($"/api/scanner/{TestHistoryVenue.WithoutHistory}/levels?symbol=LEVELA&timeframe=1h");
        HttpResponseMessage noTimeframe = await client.GetAsync($"/api/scanner/{TestHistoryVenue.WithHistory}/levels?symbol=LEVELA&timeframe=7m");

        noScanner.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await noScanner.Content.ReadAsStringAsync()).ShouldContain("scanner_venue_not_found");
        noTimeframe.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    ///     Flat at 100–101 closing at 100.5, with one swing high and one swing low a few candles back from now: 110 and 90 on hours,
    ///     120 and 95 on days. The swings sit at fixed times, so a grid boundary passing during a test leaves them inside the window.
    /// </summary>
    internal static IReadOnlyList<Candle> Shaped(Timeframe timeframe, DateTimeOffset from, DateTimeOffset to)
    {
        DateTimeOffset anchor = timeframe.FloorToGrid(DateTimeOffset.UtcNow);
        bool daily = timeframe.Key == "1d";
        var candles = new List<Candle>();

        for (DateTimeOffset time = from; time < to; time += timeframe.Duration)
        {
            long back = (anchor - time).Ticks / timeframe.Duration.Ticks;

            (decimal high, decimal low) = back switch
            {
                20 => (daily ? 120m : 110m, 100m),
                40 => (101m, daily ? 95m : 90m),
                _ => (101m, 100m)
            };

            candles.Add(new Candle(time, Open: 100.5m, high, low, Close: 100.5m, Volume: 1));
        }

        return candles;
    }

    private static decimal? Rounded(string? value)
    {
        return value is null ? null : Math.Round(decimal.Parse(value, CultureInfo.InvariantCulture), decimals: 4);
    }

    private static async Task<ScanLevelsResponse> Levels(HttpClient client, string symbol, string timeframe)
    {
        HttpResponseMessage response = await client.GetAsync($"/api/scanner/{TestHistoryVenue.WithHistory}/levels?symbol={symbol}&timeframe={timeframe}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<ScanLevelsResponse>(WebJson))!;
    }

    private async Task Replace(params ScanSnapshotRowInternal[] rows)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ReplaceScanSnapshotCommand(new ScanSnapshotInternal(TestHistoryVenue.WithHistory, DateTimeOffset.UtcNow, rows)));
    }
}