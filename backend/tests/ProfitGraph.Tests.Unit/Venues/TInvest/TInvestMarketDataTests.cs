using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;
using ProfitGraph.Venues.TInvest;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     T-Invest history, rules and search against the mock Invest API with an invented week (ADR 0023). The reference SBER
///     trades every weekday minute from 04:00 to 20:50 UTC (07:00–23:50 in Moscow) and on Sunday; GAZP trades only in the
///     main session from 07:00 to 15:40 UTC and skips every seventh minute.
/// </summary>
public sealed class TInvestMarketDataTests
{
    private static readonly Timeframe Minute = Timeframe.Parse("1m");
    private static readonly DateTimeOffset Monday = new DateTimeOffset(year: 2026, month: 10, day: 26, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly InstrumentSymbol Gazp = new InstrumentSymbol("TQBR:GAZP");
    private MockTInvestApi _api = null!;
    private MockTInvestMarket _market = null!;

    [SetUp]
    public void InventTheWeek()
    {
        _api = new MockTInvestApi();
        _market = new MockTInvestMarket(_api);
        _market.AddShare("SBER", lot: 10, increment: 0.01m, Monday.AddDays(-30));
        _market.AddShare("GAZP", lot: 10, increment: 0.01m, Monday.AddDays(-30).AddSeconds(30));
        _market.AddShare("USDX", lot: 1, increment: 0.01m, Monday.AddDays(-30), "usd");

        for (DateTimeOffset day = Monday.AddDays(-3); day < Monday.AddDays(7); day = day.AddDays(1))
        {
            Trade(day);
        }
    }

    [Test]
    public async Task ADayIsCompleted_WithQuietMinutesInsideTheSession_AndTheNightClosed()
    {
        DateTimeOffset tuesday = Monday.AddDays(1);

        HistoricalCandlesInternal history = await WithAccess(() => Source().Get(Gazp, Minute, tuesday, tuesday.AddDays(1), CancellationToken.None));

        history.Closures.ShouldBe(
            [new CandleRangeInternal(tuesday, tuesday.AddHours(4)), new CandleRangeInternal(tuesday.AddHours(20).AddMinutes(50), tuesday.AddDays(1))]);
        history.Candles.Count.ShouldBe(1010);
        history.Candles.First()
            .ShouldSatisfyAllConditions(candle => candle.NoTrades.ShouldBeTrue(), candle => candle.Close.ShouldBe(Price(Monday.AddHours(15).AddMinutes(39))));
        history.Candles.Where(candle => !candle.NoTrades).ShouldAllBe(candle => candle.Volume == 50m);
        history.Candles.Where(candle => candle.NoTrades).ShouldAllBe(candle => candle.Volume == 0m && candle.High == candle.Low);
        history.Candles.Single(candle => candle.OpenTime == tuesday.AddHours(7).AddMinutes(3)).NoTrades.ShouldBeTrue();
        _market.CandleRequests.ShouldAllBe(request => !request.HasLimit && request.CandleSourceType == GetCandlesRequest.Types.CandleSource.Exchange);
        _ = new PreparedReplay(Minute, [], history.Candles, history.Closures.Select(closure => new MarketClosure(closure.From, closure.To)));
    }

    [Test]
    public async Task TheWeekendIsClosed_EvenWhereSharesTraded()
    {
        DateTimeOffset saturday = Monday.AddDays(5);

        HistoricalCandlesInternal history = await WithAccess(() => Source().Get(Gazp, Minute, saturday, saturday.AddDays(2), CancellationToken.None));

        history.Candles.ShouldBeEmpty();
        history.Closures.ShouldBe([new CandleRangeInternal(saturday, saturday.AddDays(2))]);
    }

    [Test]
    public async Task WithoutTheCallersTInvestCredential_HistoryIsUnavailable()
    {
        var none = await Should.ThrowAsync<HistoricalDataException>(() => Source().Get(Gazp, Minute, Monday, Monday.AddHours(1), CancellationToken.None));
        using IDisposable other = MarketDataAccess.Use(new ExchangeCredentialSecretsInternal(Guid.NewGuid(), "bybit", "key", "secret"));
        var foreign = await Should.ThrowAsync<HistoricalDataException>(() => Source().Get(Gazp, Minute, Monday, Monday.AddHours(1), CancellationToken.None));

        none.Code.ShouldBe(HistoricalDataErrorCodes.Unavailable);
        foreign.Code.ShouldBe(HistoricalDataErrorCodes.Unavailable);
        _api.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task ACandleOfAnotherSource_IsInvalidProviderData()
    {
        _market.AddCandle("GAZP", Monday.AddDays(1).AddHours(2), close: 100m, source: CandleSource.DealerWeekend);

        var error = await Should.ThrowAsync<HistoricalDataException>(() =>
            WithAccess(() => Source().Get(Gazp, Minute, Monday.AddDays(1), Monday.AddDays(1).AddHours(6), CancellationToken.None)));

        error.Code.ShouldBe(HistoricalDataErrorCodes.InvalidProviderData);
    }

    [Test]
    public async Task TheFirstCandleIsTheFirstWholeIntervalOnTheGrid()
    {
        DateTimeOffset? minute = await WithAccess(() => Source().FindFirst(Gazp, Minute, Monday, CancellationToken.None));
        DateTimeOffset? hour = await WithAccess(() => Source().FindFirst(Gazp, Timeframe.Parse("1h"), Monday, CancellationToken.None));

        minute.ShouldBe(Monday.AddDays(-30).AddMinutes(1));
        hour.ShouldBe(Monday.AddDays(-30).AddHours(1));
    }

    [Test]
    public async Task RulesStepByTheLotInShares_WithExactTick_AndRoubleFees()
    {
        InstrumentRulesDefinitionInternal definition = await WithAccess(() => new TInvestInstrumentRulesSource(Shares()).Get(Gazp, CancellationToken.None));

        definition.ShouldSatisfyAllConditions(
            () => definition.BaseAsset.ShouldBe("GAZP"),
            () => definition.QuoteAsset.ShouldBe("RUB"),
            () => definition.Rules.QuantityStep.ShouldBe(10m),
            () => definition.Rules.MinQuantity.ShouldBe(10m),
            () => definition.Rules.PriceTick.ShouldBe(0.01m),
            () => definition.Rules.MinNotional.ShouldBe(0m),
            () => definition.Rules.BuyFeeAsset.ShouldBe(FeeAsset.Quote),
            () => definition.Rules.ExactQuoteFees.ShouldBeTrue());
        (await Should.ThrowAsync<InstrumentRulesException>(() =>
                WithAccess(() => new TInvestInstrumentRulesSource(Shares()).Get(new InstrumentSymbol("TQBR:USDX"), CancellationToken.None))))
            .Code.ShouldBe(InstrumentRulesErrorCodes.InvalidRequest);
        (await Should.ThrowAsync<InstrumentRulesException>(() =>
                WithAccess(() => new TInvestInstrumentRulesSource(Shares()).Get(new InstrumentSymbol("TQBR:NONE"), CancellationToken.None))))
            .Code.ShouldBe(InstrumentRulesErrorCodes.InvalidRequest);
    }

    [Test]
    public async Task SearchFindsMainBoardShares()
    {
        IReadOnlyList<InstrumentInternal> found =
            await WithAccess(() => new TInvestInstrumentCatalog(Transport(), Shares()).Search("GA", limit: 10, CancellationToken.None));

        found.ShouldBe([new InstrumentInternal("tinvest", "TQBR:GAZP", "GAZP", "RUB")]);
        await Should.ThrowAsync<InstrumentRulesException>(() => new TInvestInstrumentCatalog(Transport(), Shares()).Search("GA", limit: 10, CancellationToken.None));
    }

    [Test]
    public async Task ASymbolSearch_ReturnsTheBrandLogoSectorAndCountry()
    {
        IReadOnlyList<InstrumentInternal> found =
            await WithAccess(() => new TInvestInstrumentCatalog(Transport(), Shares()).Search("TQBR:GAZP", limit: 10, CancellationToken.None));

        InstrumentInternal share = found.ShouldHaveSingleItem();
        share.Symbol.ShouldBe("TQBR:GAZP");
        share.Appearance.ShouldNotBeNull()
            .ShouldSatisfyAllConditions(
                appearance => appearance.ImageUrl.ShouldBe("https://invest-brands.cdn-tinkoff.ru/RU000A0JR4A1x160.png"),
                appearance => appearance.Kind.ShouldBe("Invented GAZP"),
                appearance => appearance.BackgroundColor.ShouldBe("0A2E8C"),
                appearance => appearance.Description.ShouldBe(["Сектор: energy", "Страна риска: Российская Федерация"]));
        (await WithAccess(() => new TInvestInstrumentCatalog(Transport(), Shares()).Search("GA", limit: 10, CancellationToken.None)))
            .ShouldAllBe(instrument => instrument.Appearance == null);
    }

    /// <summary>
    ///     The backtest milestone of ADR 0023 at the engine: a Ladder over a week of GAZP minutes with nights, a weekend and
    ///     quiet minutes completes deterministically and fills nothing on a candle without trades.
    /// </summary>
    [Test]
    public async Task ALadderReplaysAWeekOfMinutesDeterministically()
    {
        HistoricalCandlesInternal history = await WithAccess(() => Source().Get(Gazp, Minute, Monday, Monday.AddDays(7), CancellationToken.None));
        InstrumentRulesDefinitionInternal definition = await WithAccess(() => new TInvestInstrumentRulesSource(Shares()).Get(Gazp, CancellationToken.None));
        BacktestInput input = Ladder(history, definition.Rules);

        BacktestResult first = new BacktestEngine().Run(input);
        BacktestResult second = new BacktestEngine().Run(input);

        first.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        first.Ledger.ShouldBe(second.Ledger);
        BacktestLedgerEntry[] fills = [.. first.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled)];
        fills.ShouldNotBeEmpty();
        fills.ShouldAllBe(entry => !entry.Context.Candle.NoTrades);
        fills.ShouldAllBe(entry => entry.Fill!.GrossBase % 10m == 0m);
    }

    private static BacktestInput Ladder(HistoricalCandlesInternal history, InstrumentRules rules)
    {
        CompiledTradingGraph graph = new TradingGraphCompiler()
            .Compile(LadderStrategyGraphGenerator.Generate(buyAmount: 3000m, allocationCap: 12_000m, drop: 0.02m, rise: 0.02m), Catalog)
            .Graph.ShouldNotBeNull();

        return new BacktestInput(
            graph,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 100_000m, slippage: 0m, limitFeeRate: 0.0005m, marketFeeRate: 0.0005m),
            rules,
            new PreparedReplay(
                Minute,
                history.Candles.Take(30),
                history.Candles.Skip(30),
                history.Closures.Select(closure => new MarketClosure(closure.From, closure.To))));
    }

    private static async Task<T> WithAccess<T>(Func<Task<T>> read)
    {
        using IDisposable access = MarketDataAccess.Use(
            new ExchangeCredentialSecretsInternal(
                Guid.NewGuid(),
                "tinvest",
                new Dictionary<string, string> { [TInvestCredentialFields.Token] = "t.invented-token", [TInvestCredentialFields.AccountId] = "2000000001" }));

        return await read();
    }

    /// <summary>
    ///     A price that drifts through the week and swings within each hour, so the ladder buys and sells.
    /// </summary>
    private static decimal Price(DateTimeOffset time)
    {
        var minute = (int)(time - Monday).TotalMinutes;

        return 120m + minute % 60 / 10m - minute / 1440 * 0.5m;
    }

    private void Trade(DateTimeOffset day)
    {
        bool weekday = day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        for (var minute = 0; minute < 1440; minute++)
        {
            DateTimeOffset time = day.AddMinutes(minute);
            bool referenceOpen = weekday ? minute is >= 4 * 60 and < 20 * 60 + 50 : day.DayOfWeek == DayOfWeek.Sunday && minute is >= 7 * 60 and < 16 * 60;
            bool gazpOpen = weekday ? minute is >= 7 * 60 and < 15 * 60 + 40 && minute % 7 != 3 : referenceOpen;

            if (referenceOpen)
            {
                _market.AddCandle("SBER", time, close: 300m);
            }

            if (gazpOpen)
            {
                _market.AddCandle("GAZP", time, Price(time));
            }
        }
    }

    private TInvestHistoricalCandleSource Source()
    {
        return new TInvestHistoricalCandleSource(Shares(), new TInvestCandleReader(Transport()), Options.Create(new TInvestOptions()));
    }

    private TInvestShares Shares()
    {
        return new TInvestShares(Transport(), Options.Create(new TInvestOptions()), new FakeTimeProvider(Monday));
    }

    private TInvestTransport Transport()
    {
        return new TInvestTransport(
            new MockTInvestInvokers(_api),
            new TInvestRequestLimiter(new TInvestRequestLimits { MarketData = 1000, Instruments = 1000 }, new FakeTimeProvider(Monday)),
            Options.Create(new TInvestOptions()));
    }
}