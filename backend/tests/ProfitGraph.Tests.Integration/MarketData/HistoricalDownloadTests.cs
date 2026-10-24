using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.Jobs;

namespace ProfitGraph.Tests.Integration.MarketData;

/// <summary>
///     History downloads against the run's database with the test venue's history (ADR 0010, Profit Fox Designer ADR 0017).
/// </summary>
[NonParallelizable]
public sealed class HistoricalDownloadTests
{
    private static readonly DateTimeOffset From = FakeHistorySource.DefaultFirst;
    private TestClock _clock = null!;
    private ProfitGraphApiFactory _application = null!;
    private FakeHistorySource _history = null!;
    private string _symbol = string.Empty;

    [SetUp]
    public void StartApi()
    {
        _clock = new TestClock { Now = From.AddDays(1) };
        _application = new ProfitGraphApiFactory(new Dictionary<string, string> { ["HistoricalDownload:MaxCandlesPerChunk"] = "4" }, _clock);
        _history = _application.Services.GetRequiredService<FakeHistorySource>();
        _history.Reset();
        _symbol = "T" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }

    [TearDown]
    public async Task StopApi()
    {
        await _application.DisposeAsync();
    }

    [Test]
    public async Task DownloadsOnlyMissingRanges_AndAFullCacheSkipsTheVenue()
    {
        await Append([From.AddHours(2), From.AddHours(3)]);

        HistoricalDownloadInternal first = await Download(From, From.AddHours(6));
        _history.Requests.Clear();
        HistoricalDownloadInternal second = await Download(From, From.AddHours(6));

        first.ShouldSatisfyAllConditions(
            () => first.Total.ShouldBe(6),
            () => first.Cached.ShouldBe(2),
            () => first.Downloaded.ShouldBe(4),
            () => first.Inserted.ShouldBe(4));
        second.Cached.ShouldBe(6);
        second.Inserted.ShouldBe(0);
        _history.Requests.ShouldBeEmpty();
        (await Stored(From, From.AddHours(6))).Count.ShouldBe(6);
    }

    [Test]
    public async Task ChunksAskTheVenueOnlyForTheirGaps()
    {
        await Append([From.AddHours(1)]);

        await Download(From, From.AddHours(6));

        _history.Requests.ShouldBe([(From, From.AddHours(1)), (From.AddHours(2), From.AddHours(4)), (From.AddHours(4), From.AddHours(6))]);
    }

    [Test]
    public async Task AppendNeverChangesAStoredCandle()
    {
        await Append([From], close: 107);

        int inserted = await Append([From, From.AddHours(1)]);

        inserted.ShouldBe(1);
        (await Stored(From, From.AddHours(1))).Single().Close.ShouldBe(107);
    }

    [TestCase("gap", HistoricalDataErrorCodes.Unavailable)]
    [TestCase("empty", HistoricalDataErrorCodes.Unavailable)]
    [TestCase("conflict", HistoricalDataErrorCodes.Conflict)]
    [TestCase("invalid", HistoricalDataErrorCodes.InvalidProviderData)]
    [TestCase("outside", HistoricalDataErrorCodes.InvalidProviderData)]
    public async Task InvalidVenueHistory_StoresNothingOfTheChunk(string mode, string code)
    {
        _history.Candles = (timeframe, from, to) => Broken(FakeHistorySource.Complete(timeframe, from, to), mode, timeframe);

        var error = await Should.ThrowAsync<HistoricalDataException>(() => Download(From, From.AddHours(3)));

        error.Code.ShouldBe(code);
        error.Ranges.ShouldNotBeEmpty();
        (await Stored(From, From.AddHours(3))).ShouldBeEmpty();
    }

    [Test]
    public async Task LaterChunkFailureKeepsEarlierChunks_AndARerunContinuesFromTheCache()
    {
        _history.Candles = (timeframe, from, to) => from >= From.AddHours(4) ? [] : FakeHistorySource.Complete(timeframe, from, to);

        var error = await Should.ThrowAsync<HistoricalDataException>(() => Download(From, From.AddHours(8)));
        _history.Candles = null;
        _history.Requests.Clear();
        HistoricalDownloadInternal rerun = await Download(From, From.AddHours(8));

        error.Code.ShouldBe(HistoricalDataErrorCodes.Unavailable);
        error.Message.ShouldContain(From.AddHours(4).ToString("O"));
        rerun.Cached.ShouldBe(4);
        rerun.Inserted.ShouldBe(4);
        _history.Requests.ShouldBe([(From.AddHours(4), From.AddHours(8))]);
    }

    [Test]
    public async Task InvalidStoredCandleFailsWithoutAskingTheVenue()
    {
        await Execute(
            "INSERT INTO candles VALUES (@Venue, @Symbol, '1h', @OpenTime, 100, 110, 90, 105, 1, 'csv', now())",
            new { Venue = TestHistoryVenue.WithHistory, Symbol = _symbol, OpenTime = From.AddMinutes(30) });

        var error = await Should.ThrowAsync<HistoricalDataException>(() => Download(From, From.AddHours(2)));

        error.Code.ShouldBe(HistoricalDataErrorCodes.InvalidStoredData);
        _history.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task OmittedBoundsStartAtTheVenuesFirstCandleAndEndAtTheLatestClosedOne()
    {
        _history.First = From.AddHours(20);
        _clock.Now = From.AddHours(22).AddMinutes(30);

        HistoricalDownloadInternal result = await Download(from: null, to: null);

        result.From.ShouldBe(From.AddHours(20));
        result.To.ShouldBe(From.AddHours(22));
        result.Total.ShouldBe(2);
    }

    [Test]
    public async Task FutureEndIsClampedToTheLatestClosedCandle()
    {
        _clock.Now = From.AddHours(5).AddMinutes(30);

        HistoricalDownloadInternal result = await Download(From, From.AddDays(10));

        result.To.ShouldBe(From.AddHours(5));
        (await Stored(From, From.AddDays(1))).Count.ShouldBe(5);
    }

    [Test]
    public async Task NoHistoryBeforeTheEndIsUnavailable()
    {
        _history.First = null;

        var error = await Should.ThrowAsync<HistoricalDataException>(() => Download(from: null, From.AddHours(3)));

        error.Code.ShouldBe(HistoricalDataErrorCodes.Unavailable);
    }

    [Test]
    public async Task EndpointQueuesTheDownloadAndTheWorkerCompletesIt()
    {
        using HttpClient client = await _application.CreateSignedInClient();
        using IHost worker = WorkerHost.Create();
        await worker.StartAsync();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/historical-data/downloads",
            new { venue = TestHistoryVenue.WithHistory, symbol = _symbol, timeframe = "1h", from = From, to = From.AddHours(6) });
        var queued = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        JsonNode job = await JobScenario.WaitFor(client, queued["id"]!.GetValue<Guid>(), JobScenario.Status("Completed"));
        await worker.StopAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location!.AbsolutePath.ShouldBe($"/api/jobs/{queued["id"]!.GetValue<Guid>()}");
        queued["kind"]!.GetValue<string>().ShouldBe("historical_download");
        job["result"]!["inserted"]!.GetValue<long>().ShouldBe(6);
        job["result"]!["symbol"]!.GetValue<string>().ShouldBe(_symbol);
        job["completed"]!.GetValue<long>().ShouldBe(6);
        (await Stored(From, From.AddHours(6))).Count.ShouldBe(6);
    }

    [TestCase("unknown", "BTCUSDT", "1h", "unknown_venue", "Venue")]
    [TestCase(TestHistoryVenue.WithHistory, "btc", "1h", "invalid_symbol", "Symbol")]
    [TestCase(TestHistoryVenue.WithHistory, "BTCUSDT", "4h", "unsupported_timeframe", "Timeframe")]
    [TestCase(TestHistoryVenue.WithoutHistory, "BTCUSDT", "1h", HistoricalDataErrorCodes.Unavailable, "Venue")]
    public async Task EndpointRejectsWhatCannotBeDownloaded(string venue, string symbol, string timeframe, string code, string field)
    {
        using HttpClient client = await _application.CreateSignedInClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/historical-data/downloads", new { venue, symbol, timeframe });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonNode error = (await response.Content.ReadFromJsonAsync<JsonNode>())!["errors"]![0]!;
        error["code"]!.GetValue<string>().ShouldBe(code);
        error["field"]!.GetValue<string>().ShouldBe(field);
    }

    [TestCase(arg1: 24, arg2: false, "From")]
    [TestCase(arg1: 30, arg2: true, "From")]
    [TestCase(arg1: -1, arg2: false, "From")]
    public async Task EndpointRejectsAStartThatIsNotBeforeTheLatestClosedCandle(int hour, bool misaligned, string field)
    {
        using HttpClient client = await _application.CreateSignedInClient();
        DateTimeOffset start = hour < 0 ? From.AddMinutes(30) : From.AddHours(hour).AddMinutes(misaligned ? 30 : 0);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/historical-data/downloads",
            new { venue = TestHistoryVenue.WithHistory, symbol = _symbol, timeframe = "1h", from = start });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonNode>())!["errors"]![0]!["field"]!.GetValue<string>().ShouldBe(field);
    }

    private async Task<HistoricalDownloadInternal> Download(DateTimeOffset? from, DateTimeOffset? to)
    {
        using IServiceScope scope = _application.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new DownloadCandlesCommand(TestHistoryVenue.WithHistory, _symbol, "1h", from, to), CancellationToken.None);
    }

    private async Task<int> Append(IEnumerable<DateTimeOffset> times, decimal close = 105)
    {
        using IServiceScope scope = _application.Services.CreateScope();
        CandleSeries series = Series(scope);
        Candle[] candles = [.. times.Select(time => new Candle(time, Open: 100, High: 110, Low: 90, close, Volume: 1))];

        return await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                () => scope.ServiceProvider.GetRequiredService<ICandleRepository>().Append(series, candles, "test", _clock.Now, CancellationToken.None),
                CancellationToken.None);
    }

    private async Task<IReadOnlyList<Candle>> Stored(DateTimeOffset from, DateTimeOffset to)
    {
        using IServiceScope scope = _application.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ICandleRepository>().Get(Series(scope), from, to, limit: 10_000, CancellationToken.None);
    }

    private CandleSeries Series(IServiceScope scope)
    {
        return scope.ServiceProvider.GetRequiredService<VenueRegistry>().ResolveSeries(TestHistoryVenue.WithHistory, _symbol, "1h");
    }

    private async Task Execute(string sql, object parameters)
    {
        using IServiceScope scope = _application.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NpgsqlConnection>().ExecuteAsync(sql, parameters);
    }

    private static IReadOnlyList<Candle> Broken(IReadOnlyList<Candle> candles, string mode, Timeframe timeframe)
    {
        List<Candle> broken = [.. candles];

        switch (mode)
        {
            case "gap":
                broken.RemoveAt(1);

                break;
            case "empty":
                broken.Clear();

                break;
            case "conflict":
                broken.Add(broken[0] with { Close = 106 });

                break;
            case "invalid":
                broken[0] = broken[0] with { High = 1 };

                break;
            case "outside":
                broken.Add(broken[^1] with { OpenTime = broken[^1].OpenTime + timeframe.Duration });

                break;
        }

        return broken;
    }
}