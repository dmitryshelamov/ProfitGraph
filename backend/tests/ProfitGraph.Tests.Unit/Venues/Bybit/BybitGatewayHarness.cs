using MediatR;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     The Bybit order gateway wired to <see cref="MockBybitExchange" /> with one bot on BTCUSDT and an invented credential.
/// </summary>
internal sealed class BybitGatewayHarness
{
    public static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 0.0001m,
        priceTick: 0.01m,
        minQuantity: 0.0001m,
        minNotional: 5m,
        basePrecision: 4,
        quotePrecision: 8);

    public MockBybitExchange Exchange { get; } = new MockBybitExchange();
    public Bot Bot { get; }
    public BybitOrderGateway Gateway { get; }
    public BybitOrderUpdates Updates { get; } = new BybitOrderUpdates();
    public BybitFeeRateReader FeeRates { get; }

    public BybitGatewayHarness()
    {
        Bot = Bot.Create(
            BotId.New(),
            UserId.New(),
            StrategyId.New(),
            StrategyVersionId.New(),
            ExchangeCredentialId.New(),
            new CandleSeries(new VenueId("bybit"), new InstrumentSymbol("BTCUSDT"), Timeframe.Parse("1h")),
            new BotParameters(new Dictionary<string, decimal>()),
            allocationCap: 100m,
            new BotRiskLimits(maxOrderQuote: 20m, dailyLossLimitQuote: 10m),
            new BotFeeRates(limitRate: 0.001m, marketRate: 0.001m, DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow);
        var bots = Substitute.For<IBotRepository>();
        bots.Get(Bot.Id, Arg.Any<CancellationToken>()).Returns(Bot);
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ReadExchangeCredentialSecretsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ExchangeCredentialSecretsInternal(Bot.CredentialId.Value, "bybit", MockBybitExchange.ApiKey, MockBybitExchange.ApiSecret));
        var rules = Substitute.For<IInstrumentRulesSource>();
        rules.Venue.Returns(new VenueId("bybit"));
        rules.Get(Arg.Any<InstrumentSymbol>(), Arg.Any<CancellationToken>())
            .Returns(
                new InstrumentRulesDefinitionInternal(
                    "bybit",
                    "test",
                    "spot",
                    "BTCUSDT",
                    "BTC",
                    "USDT",
                    BasePrecisionIncrement: 0.0001m,
                    QuotePrecisionIncrement: 0.01m,
                    Rules));
        ServiceProvider services = new ServiceCollection().AddSingleton(bots).AddSingleton(sender).AddSingleton(rules).BuildServiceProvider();
        var http = new HttpClient(Exchange) { BaseAddress = new Uri("https://bybit.invalid/") };
        var clock = new BybitServerClock(TimeProvider.System);
        var read = new BybitReadClient(http, clock);
        Gateway = new BybitOrderGateway(
            new BybitTradeClient(http, clock),
            read,
            new BybitOrderReads(read),
            new BybitBotContexts(services.GetRequiredService<IServiceScopeFactory>()),
            Updates);
        FeeRates = new BybitFeeRateReader(read);
    }

    public static OrderSnapshot MarketBuy(long id, decimal quoteBudget)
    {
        return new OrderSnapshot(
            id,
            "entry",
            OrderSide.Buy,
            OrderType.Market,
            Quantity: null,
            LimitPrice: null,
            quoteBudget,
            quoteBudget,
            "entry",
            EligibleFromCandleIndex: 0,
            OrderStatus.PendingSubmit);
    }

    public static OrderSnapshot LimitSell(long id, decimal quantity, decimal price)
    {
        return new OrderSnapshot(
            id,
            "exit",
            OrderSide.Sell,
            OrderType.Limit,
            quantity,
            price,
            QuoteBudget: null,
            quantity,
            "exit",
            EligibleFromCandleIndex: 0,
            OrderStatus.PendingSubmit);
    }

    public async Task<List<ExternalOrderUpdate>> Published(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var published = new List<ExternalOrderUpdate>();

        await foreach (ExternalOrderUpdate update in Updates.Read(Bot.Id, timeout.Token))
        {
            published.Add(update);

            if (published.Count == count)
            {
                break;
            }
        }

        return published;
    }
}