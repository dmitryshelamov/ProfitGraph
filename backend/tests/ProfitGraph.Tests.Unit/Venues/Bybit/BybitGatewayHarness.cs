using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotOrdersToRefresh;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Venues.Bybit;
using ProfitGraph.Venues.Bybit.MarketData;
using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     The Bybit order gateway wired to <see cref="MockBybitExchange" /> with one bot on BTCUSDT and an invented credential; on USDT
///     perpetuals (ADR 0061) the bot runs a strategy of the given direction at the given leverage.
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

    public static readonly InstrumentRules LinearRules = new InstrumentRules(
        quantityStep: 0.001m,
        priceTick: 0.1m,
        minQuantity: 0.001m,
        minNotional: 5m,
        basePrecision: 3,
        quotePrecision: 8,
        FeeAsset.Quote,
        exactQuoteFees: true);

    public MockBybitExchange Exchange { get; } = new MockBybitExchange();
    public Bot Bot { get; }
    public BybitOrderGateway Gateway { get; }
    public BybitOrderUpdates Updates { get; } = new BybitOrderUpdates();
    public BybitFeeRateReader FeeRates { get; }
    public FakeBybitPrivateTransport PrivateStream { get; } = new FakeBybitPrivateTransport();
    public BybitReadClient ReadClient { get; }

    public FakeTimeProvider Time { get; } = new FakeTimeProvider(new DateTimeOffset(year: 2026, month: 10, day: 27, hour: 12, minute: 0, second: 0, TimeSpan.Zero));

    /// <summary>
    ///     The orders the bot's engine and intents hold open, which a restored private stream reads again (ADR 0044).
    /// </summary>
    public List<ReconcileOrderInternal> OrdersToRefresh { get; } = [];

    /// <summary>
    ///     A lost private stream connects again every second of <see cref="Time" /> and gives up after ten.
    /// </summary>
    public PrivateStreamReconnectPolicy Reconnect { get; } = new PrivateStreamReconnectPolicy { Delays = [TimeSpan.FromSeconds(1)], Budget = TimeSpan.FromSeconds(10) };

    public BybitGatewayHarness(bool linear = false, PositionDirection direction = PositionDirection.Long, decimal leverage = 1m)
    {
        BybitMarket market = linear ? BybitMarket.Linear : BybitMarket.Spot;
        Exchange.Linear = linear;
        Bot = Bot.Create(
            BotId.New(),
            UserId.New(),
            StrategyId.New(),
            StrategyVersionId.New(),
            ExchangeCredentialId.New(),
            new CandleSeries(market.Venue, new InstrumentSymbol("BTCUSDT"), Timeframe.Parse("1h")),
            new BotParameters(new Dictionary<string, decimal>()),
            allocationCap: 100m,
            new BotRiskLimits(maxOrderQuote: 20m, dailyLossLimitQuote: 10m),
            new BotFeeRates(limitRate: 0.001m, marketRate: 0.001m, DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow,
            linear ? BybitLinearBotSettingsSchema.Write(leverage) : null);
        var strategies = Substitute.For<IStrategyRepository>();
        StrategyDefinition definition = direction == PositionDirection.Short ? LevelTradeShortStrategyBlockFactory.Create() : LevelTradeStrategyBlockFactory.Create();
        strategies.GetVersions(Bot.StrategyId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new StrategyVersion(
                    Bot.StrategyVersionId,
                    Bot.StrategyId,
                    StrategyVersionNumber.First,
                    definition,
                    new GraphLayout(new Dictionary<string, NodePosition>()),
                    DateTimeOffset.UtcNow)
            ]);
        var bots = Substitute.For<IBotRepository>();
        bots.Get(Bot.Id, Arg.Any<CancellationToken>()).Returns(Bot);
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ReadExchangeCredentialSecretsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ExchangeCredentialSecretsInternal(Bot.CredentialId.Value, "bybit", MockBybitExchange.ApiKey, MockBybitExchange.ApiSecret));
        sender.Send(Arg.Any<GetBotOrdersToRefreshQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => new BotReconcileInternal(Bot.Id, AllowForeignOrders: false, [.. OrdersToRefresh]));
        var rules = Substitute.For<IInstrumentRulesSource>();
        rules.Venue.Returns(market.Venue);
        rules.Get(Arg.Any<InstrumentSymbol>(), Arg.Any<CancellationToken>())
            .Returns(
                new InstrumentRulesDefinitionInternal(
                    market.Venue.Value,
                    "test",
                    market.Name,
                    "BTCUSDT",
                    "BTC",
                    "USDT",
                    BasePrecisionIncrement: 0.0001m,
                    QuotePrecisionIncrement: 0.01m,
                    linear ? LinearRules : Rules));
        ServiceProvider services = new ServiceCollection().AddSingleton(bots)
            .AddSingleton(sender)
            .AddSingleton(rules)
            .AddSingleton(strategies)
            .BuildServiceProvider();
        var http = new HttpClient(Exchange) { BaseAddress = new Uri("https://bybit.invalid/") };

        // A frozen host clock makes Bybit's server time exact, so signed timestamps and auth expiries are predictable.
        var clock = new BybitServerClock(new BybitOptions(), new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddDays(1)));
        var read = new BybitReadClient(http, clock);
        ReadClient = read;
        var reads = new BybitOrderReads(read);
        Gateway = new BybitOrderGateway(
            new BybitTradeClient(http, clock),
            read,
            reads,
            new BybitPrivateStreams(
                PrivateStream,
                read,
                reads,
                Updates,
                services.GetRequiredService<IServiceScopeFactory>(),
                Reconnect,
                new BybitOptions(),
                Time,
                NullLogger<BybitPrivateStreams>.Instance),
            new BybitBotContexts(services.GetRequiredService<IServiceScopeFactory>()),
            Updates,
            new BybitAbsenceProofs(reads, new BybitOptions(), Time),
            market);
        FeeRates = new BybitFeeRateReader(read, market);
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

    /// <summary>
    ///     What else the bot's updates receive within a short wait.
    /// </summary>
    public async Task<List<ExternalOrderUpdate>> PublishedSoon()
    {
        using var wait = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var published = new List<ExternalOrderUpdate>();

        try
        {
            await foreach (ExternalOrderUpdate update in Updates.Read(Bot.Id, wait.Token))
            {
                published.Add(update);
            }
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
            // Nothing else arrived.
        }

        return published;
    }

    /// <summary>
    ///     Moves <see cref="Time" /> a second at a time until the condition holds, so the private stream's pauses before it
    ///     connects again pass.
    /// </summary>
    public async Task AdvanceUntil(Func<bool> condition)
    {
        for (var step = 0; step < 400 && !condition(); step++)
        {
            Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        condition().ShouldBeTrue();
    }
}