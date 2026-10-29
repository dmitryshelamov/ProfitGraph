using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.MarketData.Contracts;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Charts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Fills;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The bot's chart over HTTP (ADR 0026) on the test venue: pages of closed candles of the bot's own series, brought into
///     the cache from the venue once, the venue's candles of an incomplete history shown without being stored, the stored
///     candles when the venue fails, and the bot's fills of the same range. Values are invented; no venue is contacted.
/// </summary>
[NonParallelizable]
public sealed class BotCandlesTests
{
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateTimeOffset First = FakeHistorySource.DefaultFirst;

    private ProfitGraphApiFactory _factory = null!;
    private FakeHistorySource _history = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ProfitGraphApiFactory(BotApi.Settings());
        _history = _factory.Services.GetRequiredService<FakeHistorySource>();
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _factory.DisposeAsync();
    }

    [SetUp]
    public void ResetVenue()
    {
        _history.Reset();
    }

    [Test]
    public async Task APageIsReadFromTheVenueOnce_AndCarriesTheBotsFillsOfItsRange()
    {
        (BotOwner owner, Guid id, string symbol) = await NewBot();
        await Record(
            id,
            Fill(id, engineOrderId: 1, OrderSide.Buy, First.AddMinutes(90), grossBase: 0.5m, grossQuote: 50m),
            Fill(id, engineOrderId: 2, OrderSide.Sell, First.AddHours(3), grossBase: 0.25m, grossQuote: 30m),
            Fill(id, engineOrderId: 3, OrderSide.Sell, First.AddHours(6), grossBase: 0.25m, grossQuote: 31m));

        BotCandlesResponse page = await Page(owner, id, First, First.AddHours(5));
        BotCandlesResponse again = await Page(owner, id, First, First.AddHours(5));

        page.Venue.ShouldBe(TestHistoryVenue.WithHistory);
        page.Symbol.ShouldBe(symbol);
        page.Timeframe.ShouldBe("1h");
        page.Status.ShouldBe(CandleHistoryStatus.Complete);
        page.ErrorCode.ShouldBeNull();
        page.From.ShouldBe(First);
        page.To.ShouldBe(First.AddHours(5));
        page.Candles.Select(candle => candle.OpenTime).ShouldBe([.. Enumerable.Range(start: 0, count: 5).Select(hour => First.AddHours(hour))]);
        page.Fills.Select(fill => (fill.Side, fill.Price, fill.GrossBase)).ShouldBe([(OrderSide.Buy, "100", "0.5"), (OrderSide.Sell, "120", "0.25")]);
        page.Fills[0].FilledAt.ShouldBe(First.AddMinutes(90));
        page.FillsTruncated.ShouldBeFalse();
        again.Candles.ShouldBe(page.Candles);
        _history.Requests.ShouldBe([(First, First.AddHours(5))]);
        (await Stored(owner, symbol, First, First.AddHours(5))).Length.ShouldBe(5);
    }

    [Test]
    public async Task ARangeIsWidenedToTheGrid_AndCutAtTheLatestClosedCandle()
    {
        (BotOwner owner, Guid id, _) = await NewBot();
        DateTimeOffset latest = Timeframe.Parse("1h").FloorToGrid(DateTimeOffset.UtcNow);

        BotCandlesResponse widened = await Page(owner, id, First.AddMinutes(30), First.AddMinutes(150));
        BotCandlesResponse cut = await Page(owner, id, latest.AddHours(-2), latest.AddHours(5));

        widened.From.ShouldBe(First);
        widened.To.ShouldBe(First.AddHours(3));
        widened.Candles.Count.ShouldBe(3);
        cut.To.ShouldBe(latest);
        cut.LatestClosed.ShouldBe(latest);
        cut.Candles.Select(candle => candle.OpenTime).ShouldBe([latest.AddHours(-2), latest.AddHours(-1)]);
    }

    [Test]
    public async Task AHistoryWithIntervalsWithoutCandlesIsShownButNotStored()
    {
        (BotOwner owner, Guid id, string symbol) = await NewBot();
        _history.Candles = (timeframe, from, to) =>
        [
            .. FakeHistorySource.Complete(timeframe, from, to).Where(candle => candle.OpenTime != First.AddHours(2))
        ];

        BotCandlesResponse page = await Page(owner, id, First, First.AddHours(4));

        page.Status.ShouldBe(CandleHistoryStatus.Partial);
        page.ErrorCode.ShouldBeNull();
        page.Candles.Select(candle => candle.OpenTime).ShouldBe([First, First.AddHours(1), First.AddHours(3)]);
        (await Stored(owner, symbol, First, First.AddHours(4))).ShouldBeEmpty();
    }

    [Test]
    public async Task AVenueThatFailsLeavesTheStoredCandles()
    {
        (BotOwner owner, Guid id, _) = await NewBot();
        await Page(owner, id, First, First.AddHours(2));
        _history.BeforeGet = _ => throw new HistoricalDataException(HistoricalDataErrorCodes.ProviderUnavailable, "The venue is down.", []);

        BotCandlesResponse page = await Page(owner, id, First, First.AddHours(4));

        page.Status.ShouldBe(CandleHistoryStatus.Unavailable);
        page.ErrorCode.ShouldBe(HistoricalDataErrorCodes.ProviderUnavailable);
        page.Candles.Select(candle => candle.OpenTime).ShouldBe([First, First.AddHours(1)]);
    }

    [Test]
    public async Task APageIsLimitedAndPrivateToTheBotsOwner()
    {
        (BotOwner owner, Guid id, _) = await NewBot();
        BotOwner other = await BotApi.NewOwner(_factory);

        HttpResponseMessage tooLong = await owner.Client.GetAsync(Url(id, First, First.AddHours(1001)));
        HttpResponseMessage local = await owner.Client.GetAsync($"{BotApi.Path}/{id}/candles?from=2026-01-01T03:00:00%2B03:00&to=2026-01-01T05:00:00Z");
        HttpResponseMessage foreign = await other.Client.GetAsync(Url(id, First, First.AddHours(2)));

        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        local.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        _history.Requests.ShouldBeEmpty();
    }

    private static BotFill Fill(Guid botId, long engineOrderId, OrderSide side, DateTimeOffset at, decimal grossBase, decimal grossQuote)
    {
        return BotFill.Record(
            BotFillId.New(),
            new BotId(botId),
            engineOrderId,
            side,
            tag: null,
            grossBase,
            grossQuote,
            fee: 0m,
            grossBase,
            at);
    }

    private static string Url(Guid id, DateTimeOffset from, DateTimeOffset to)
    {
        return $"{BotApi.Path}/{id}/candles?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
    }

    private static async Task<BotCandlesResponse> Page(BotOwner owner, Guid id, DateTimeOffset from, DateTimeOffset to)
    {
        HttpResponseMessage response = await owner.Client.GetAsync(Url(id, from, to));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<BotCandlesResponse>(WebJson))!;
    }

    private static async Task<CandleResponse[]> Stored(BotOwner owner, string symbol, DateTimeOffset from, DateTimeOffset to)
    {
        return (await owner.Client.GetFromJsonAsync<CandleResponse[]>(
            $"/api/historical-data/candles?venue={TestHistoryVenue.WithHistory}&symbol={symbol}&timeframe=1h&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}",
            WebJson))!;
    }

    /// <summary>
    ///     A bot on its own symbol: candles are a cache shared by every user.
    /// </summary>
    private async Task<(BotOwner Owner, Guid Id, string Symbol)> NewBot()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        string symbol = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant() + "USDT";
        HttpResponseMessage created = await owner.Client.PostAsJsonAsync(BotApi.Path, BotApi.Request(owner, symbol: symbol));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return (owner, (await BotApi.Json(created)).GetProperty("id").GetGuid(), symbol);
    }

    private async Task Record(Guid botId, params BotFill[] fills)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    foreach (BotFill fill in fills)
                    {
                        await scope.ServiceProvider.GetRequiredService<IBotFillRepository>().TryAdd(fill, CancellationToken.None);
                    }

                    return true;
                },
                CancellationToken.None);
    }
}