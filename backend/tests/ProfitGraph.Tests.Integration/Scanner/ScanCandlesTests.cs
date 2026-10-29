using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.MarketData.Charts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     A scanner pair's chart over HTTP (ADR 0059) on the test venue: a page is brought into the shared cache from the venue once
///     and cut at the latest closed candle; a venue without a scanner and a timeframe the venue lacks are refused. Values are
///     invented; no venue is contacted.
/// </summary>
[NonParallelizable]
public sealed class ScanCandlesTests
{
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateTimeOffset First = FakeHistorySource.DefaultFirst;

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
    public void ResetVenue()
    {
        _history.Reset();
    }

    [Test]
    public async Task APageIsReadFromTheVenueOnce()
    {
        using HttpClient client = await _api.CreateSignedInClient();

        ScanCandlesResponse page = await Page(client, "SCANA", First, First.AddHours(5));
        ScanCandlesResponse again = await Page(client, "SCANA", First, First.AddHours(5));

        (page.Venue, page.Symbol, page.Timeframe, page.Status).ShouldBe((TestHistoryVenue.WithHistory, "SCANA", "1h", CandleHistoryStatus.Complete));
        page.Candles.Select(candle => candle.OpenTime).ShouldBe([.. Enumerable.Range(start: 0, count: 5).Select(hour => First.AddHours(hour))]);
        again.Candles.Count.ShouldBe(5);
        _history.Requests.ShouldBe([(First, First.AddHours(5))]);
    }

    [Test]
    public async Task APageEndsAtTheLatestClosedCandle()
    {
        using HttpClient client = await _api.CreateSignedInClient();
        var latest = new DateTimeOffset(DateTime.UtcNow.Ticks - DateTime.UtcNow.Ticks % TimeSpan.TicksPerHour, TimeSpan.Zero);

        ScanCandlesResponse page = await Page(client, "SCANB", latest.AddHours(-2), latest.AddHours(5));

        page.To.ShouldBe(page.LatestClosed);
        page.Candles.ShouldAllBe(candle => candle.OpenTime < page.LatestClosed);
    }

    [Test]
    public async Task RefusesAVenueWithoutAScannerAndAnUnknownTimeframe()
    {
        using HttpClient client = await _api.CreateSignedInClient();

        HttpResponseMessage noScanner = await client.GetAsync(Url(TestHistoryVenue.WithoutHistory, "SCANA", "1h", First, First.AddHours(1)));
        HttpResponseMessage noTimeframe = await client.GetAsync(Url(TestHistoryVenue.WithHistory, "SCANA", "7m", First, First.AddHours(1)));

        noScanner.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await noScanner.Content.ReadAsStringAsync()).ShouldContain("scanner_venue_not_found");
        noTimeframe.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<ScanCandlesResponse> Page(HttpClient client, string symbol, DateTimeOffset from, DateTimeOffset to)
    {
        HttpResponseMessage response = await client.GetAsync(Url(TestHistoryVenue.WithHistory, symbol, "1h", from, to));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<ScanCandlesResponse>(WebJson))!;
    }

    private static string Url(string venue, string symbol, string timeframe, DateTimeOffset from, DateTimeOffset to)
    {
        return
            $"/api/scanner/{venue}/candles?symbol={symbol}&timeframe={timeframe}&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
    }
}