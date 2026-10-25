using Grpc.Core;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.TInvest;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.LiveData;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Trading;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

using Candle = ProfitGraph.Engine.Market.Candle;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     What a T-Invest bot reads live (ADR 0023): its commission rate priced by <c>GetOrderPrice</c>, its account value in
///     roubles and its candles by polling with the history rules. Values are invented; the API is the in-process mock.
/// </summary>
public sealed class TInvestLiveReadsTests
{
    private static readonly DateTimeOffset Tuesday = new DateTimeOffset(year: 2026, month: 10, day: 27, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly ExchangeCredentialSecretsInternal Credential = new ExchangeCredentialSecretsInternal(
        Guid.NewGuid(),
        "tinvest",
        new Dictionary<string, string> { [TInvestCredentialFields.Token] = "t.invented-token", [TInvestCredentialFields.AccountId] = "2000000001" });

    private MockTInvestApi _api = null!;
    private FakeTimeProvider _clock = null!;
    private MockTInvestMarket _market = null!;

    [SetUp]
    public void CreateMock()
    {
        _api = new MockTInvestApi();
        _market = new MockTInvestMarket(_api);
        _clock = new FakeTimeProvider(Tuesday.AddHours(8));
        _market.AddShare("SBER", lot: 10, increment: 0.01m, Tuesday.AddDays(-30));
        _market.AddShare("GAZP", lot: 10, increment: 0.01m, Tuesday.AddDays(-30));
    }

    [Test]
    public async Task TheCommissionRateIsTheDealCommissionOfAProbeOrderOverItsAmount()
    {
        GetOrderPriceRequest? priced = null;
        _api.On<GetLastPricesRequest, GetLastPricesResponse>(MarketDataService.Descriptor, "GetLastPrices", request => LastPrice(request, price: 125m));
        _api.On<GetOrderPriceRequest, GetOrderPriceResponse>(
            OrdersService.Descriptor,
            "GetOrderPrice",
            request =>
            {
                priced = request;

                return new GetOrderPriceResponse { InitialOrderAmount = Money(100_000m), DealCommission = Money(50m) };
            });

        VenueFeeRatesInternal rates = await Fees().Read(Credential, new InstrumentSymbol("TQBR:GAZP"), CancellationToken.None);

        rates.ShouldBe(new VenueFeeRatesInternal(MakerRate: 0.0005m, TakerRate: 0.0005m));
        GetOrderPriceRequest probe = priced.ShouldNotBeNull();
        probe.ShouldSatisfyAllConditions(
            () => probe.AccountId.ShouldBe("2000000001"),
            () => probe.Direction.ShouldBe(OrderDirection.Buy),
            () => probe.Quantity.ShouldBe(80),
            () => probe.Price.ToDecimal().ShouldBe(125m));
    }

    [Test]
    public async Task WithoutAPricedCommission_TheRateIsUnavailable()
    {
        _api.On<GetLastPricesRequest, GetLastPricesResponse>(MarketDataService.Descriptor, "GetLastPrices", request => LastPrice(request, price: 125m));
        _api.On<GetOrderPriceRequest, GetOrderPriceResponse>(
            OrdersService.Descriptor,
            "GetOrderPrice",
            _ => new GetOrderPriceResponse { InitialOrderAmount = Money(0m) });
        await Should.ThrowAsync<VenueFeeRatesUnavailableException>(() => Fees().Read(Credential, new InstrumentSymbol("TQBR:GAZP"), CancellationToken.None));

        _api.On<GetOrderPriceRequest, GetOrderPriceResponse>(
            OrdersService.Descriptor,
            "GetOrderPrice",
            _ => throw MockTInvestApi.Refusal(StatusCode.Unavailable, string.Empty));
        await Should.ThrowAsync<VenueFeeRatesUnavailableException>(() => Fees().Read(Credential, new InstrumentSymbol("TQBR:GAZP"), CancellationToken.None));
    }

    [Test]
    public async Task TheAccountIsValuedInRoubles_AndAFailureIsReportedByKind()
    {
        _api.On<PortfolioRequest, PortfolioResponse>(
            OperationsService.Descriptor,
            "GetPortfolio",
            request => request.AccountId == "2000000001" && request.Currency == PortfolioRequest.Types.CurrencyRequest.Rub
                ? new PortfolioResponse
                {
                    TotalAmountPortfolio = Money(123_456.78m),
                    Positions = { new PortfolioPosition { Ticker = "GAZP", Quantity = 30m.ToQuotation() } }
                }
                : throw MockTInvestApi.Refusal(StatusCode.NotFound, "50004"));
        var reader = new TInvestAccountBalanceReader(Transport(), _clock);

        AccountBalanceReadInternal read = await reader.Read(Credential, CancellationToken.None);

        read.Observation!.ShouldSatisfyAllConditions(
            observation => observation.TotalValue.ShouldBe(123_456.78m),
            observation => observation.ValueCurrency.ShouldBe("RUB"),
            observation => observation.AvailableValue.ShouldBeNull(),
            observation => observation.Coins.ShouldBe([new AccountCoinBalanceInternal("GAZP", Balance: 30m, UsdValue: null)]));
        _api.On<PortfolioRequest, PortfolioResponse>(
            OperationsService.Descriptor,
            "GetPortfolio",
            _ => throw MockTInvestApi.Refusal(StatusCode.Unavailable, string.Empty));
        (await reader.Read(Credential, CancellationToken.None)).FailureReason.ShouldBe("venue_error");
    }

    [Test]
    public async Task LiveCandlesArePublishedAfterTheGrace_AndATradeInAPublishedQuietMinuteIsACorrection()
    {
        for (var minute = 0; minute < 120; minute++)
        {
            DateTimeOffset time = Tuesday.AddHours(7).AddMinutes(minute);
            _market.AddCandle("SBER", time, close: 300m);

            if (minute != 61)
            {
                _market.AddCandle("GAZP", time, 120m + minute / 100m);
            }
        }

        _clock.SetUtcNow(Tuesday.AddHours(8).AddMinutes(1).AddSeconds(30));
        using IDisposable access = MarketDataAccess.Use(Credential);
        await using IAsyncEnumerator<Candle> feed = Live()
            .Subscribe(new InstrumentSymbol("TQBR:GAZP"), Timeframe.Parse("1m"), Tuesday.AddHours(7).AddMinutes(59), CancellationToken.None)
            .GetAsyncEnumerator();

        (await feed.MoveNextAsync()).ShouldBeTrue();
        feed.Current.OpenTime.ShouldBe(Tuesday.AddHours(8));
        _clock.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(25)));
        Candle[] next = [await Next(feed), await Next(feed)];
        next.Select(candle => (candle.OpenTime, candle.NoTrades)).ShouldBe([(Tuesday.AddHours(8).AddMinutes(1), true), (Tuesday.AddHours(8).AddMinutes(2), false)]);

        _market.AddCandle("GAZP", Tuesday.AddHours(8).AddMinutes(1), close: 121m);
        _clock.Advance(TimeSpan.FromMinutes(1));

        (await Should.ThrowAsync<OrderGatewaySafetyException>(async () => await feed.MoveNextAsync())).Reason.ShouldBe(BotStatusReason.HistoryDataCorrection);
    }

    private static async Task<Candle> Next(IAsyncEnumerator<Candle> feed)
    {
        (await feed.MoveNextAsync()).ShouldBeTrue();

        return feed.Current;
    }

    private static GetLastPricesResponse LastPrice(GetLastPricesRequest request, decimal price)
    {
        return new GetLastPricesResponse { LastPrices = { new LastPrice { InstrumentUid = request.InstrumentId.Single(), Price = price.ToQuotation() } } };
    }

    private static MoneyValue Money(decimal value)
    {
        var quotation = value.ToQuotation();

        return new MoneyValue { Currency = "rub", Units = quotation.Units, Nano = quotation.Nano };
    }

    private TInvestFeeRateReader Fees()
    {
        TInvestTransport transport = Transport();

        return new TInvestFeeRateReader(new TInvestShares(transport, _clock), new TInvestPrices(transport), transport, Options.Create(new TInvestOptions()));
    }

    private TInvestLiveCandleSource Live()
    {
        TInvestTransport transport = Transport();
        IOptions<TInvestOptions> options = Options.Create(new TInvestOptions());

        return new TInvestLiveCandleSource(
            new TInvestHistoricalCandleSource(new TInvestShares(transport, _clock), new TInvestCandleReader(transport), options),
            options,
            _clock);
    }

    private TInvestTransport Transport()
    {
        return new TInvestTransport(
            new MockTInvestInvokers(_api),
            new TInvestRequestLimiter(new TInvestRequestLimits { MarketData = 1000, Instruments = 1000 }, _clock),
            Options.Create(new TInvestOptions()));
    }
}