using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotOrdersToRefresh;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Trading;
using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     The T-Invest order gateway and its waiting worker for one invented bot, over the in-process mock API, an in-memory
///     attempts table and a recorder of what reaches the bot runtime.
/// </summary>
internal sealed class TInvestGatewayHarness
{
    public const string AccountId = "2000000001";
    public const string ShareUid = "e6123145-9665-43e0-8413-cd61b8aa9b13";
    public const string Exchange = "moex_mrng_evng_e_wknd_dlr";

    public static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 27, hour: 8, minute: 0, second: 0, TimeSpan.Zero);

    public BotId Bot { get; } = new BotId(Guid.Parse("4f1d2c3b-5a69-4788-9a0b-1c2d3e4f5a6b"));

    public MockTInvestApi Api { get; } = new MockTInvestApi();

    public MockTInvestOrders Orders { get; }

    public MemoryTInvestOrderAttempts Attempts { get; } = new MemoryTInvestOrderAttempts();

    public TInvestOrderUpdates Updates { get; } = new TInvestOrderUpdates();

    public RecordingTInvestOrderWaits Waits { get; } = new RecordingTInvestOrderWaits();

    public FakeTimeProvider Clock { get; } = new FakeTimeProvider(Now);

    public TInvestBotContext Context { get; }

    public TInvestOrderProgress Progress { get; }

    /// <summary>
    ///     What the bots on the account and share hold by their checkpoints; the harness bot holds nothing to begin with.
    /// </summary>
    public FixedTInvestBotPositions Positions { get; }

    public TInvestOrderGateway Gateway { get; }

    public TInvestOrderStreams Streams { get; }

    public TInvestWaitingOrdersWorker Worker { get; }

    public TInvestClosedSessionSweep Sweep { get; }

    /// <summary>
    ///     The orders the bot's engine and intents hold open, which a restored order state stream reads again (ADR 0044).
    /// </summary>
    public List<ReconcileOrderInternal> OrdersToRefresh { get; } = [];

    public TInvestGatewayHarness(string exchange = Exchange)
    {
        Orders = new MockTInvestOrders(Api, AccountId) { PositionInstrumentUid = ShareUid };
        IOptions<TInvestOptions> options = Options.Create(new TInvestOptions());
        var transport = new TInvestTransport(
            new MockTInvestInvokers(Api),
            new TInvestRequestLimiter(new TInvestRequestLimits { MarketData = 1000, Instruments = 1000, Orders = 1000, Users = 1000 }, Clock),
            options);
        var reads = new TInvestOrderReads(transport);
        TInvestSymbol.TryParse("TQBR:SBER", out TInvestSymbol? symbol).ShouldBeTrue();
        var share = new TInvestShare(
            symbol,
            ShareUid,
            "Invented SBER",
            Lot: 10m,
            PriceIncrement: 0.01m,
            FirstMinuteCandle: null,
            FirstDayCandle: null,
            exchange);
        Context = new TInvestBotContext(Bot, new TInvestToken("t.invented-token"), AccountId, share);
        var contexts = new FixedTInvestBotContexts(Context);
        var schedules = new TInvestSchedules(transport, options, Clock, NullLogger<TInvestSchedules>.Instance);
        var operations = new TInvestOperationReads(transport, options);
        Positions = new FixedTInvestBotPositions(Bot);
        var coverage = new TInvestPositionCoverage(operations, Positions, NullLogger<TInvestPositionCoverage>.Instance);
        Progress = new TInvestOrderProgress(Attempts, Waits, schedules, coverage, Clock);
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetBotOrdersToRefreshQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => new BotReconcileInternal(Bot, AllowForeignOrders: false, [.. OrdersToRefresh]));
        ServiceProvider services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();

        // A lost order state stream subscribes again every second of the clock and gives up after ten.
        Streams = new TInvestOrderStreams(
            transport,
            reads,
            Attempts,
            Updates,
            Progress,
            services.GetRequiredService<IServiceScopeFactory>(),
            new PrivateStreamReconnectPolicy { Delays = [TimeSpan.FromSeconds(1)], Budget = TimeSpan.FromSeconds(10) },
            options,
            Clock,
            NullLogger<TInvestOrderStreams>.Instance);
        var proofs = new TInvestAbsenceProofs(
            reads,
            operations,
            Attempts,
            schedules,
            options,
            Clock,
            NullLogger<TInvestAbsenceProofs>.Instance);
        Gateway = new TInvestOrderGateway(
            contexts,
            Attempts,
            reads,
            Streams,
            Updates,
            new TInvestPrices(transport),
            new TInvestTradingStatuses(transport),
            new TInvestPriceBands(transport, options, Clock),
            schedules,
            Waits,
            Progress,
            proofs,
            transport,
            options,
            Clock);
        Worker = new TInvestWaitingOrdersWorker(
            Attempts,
            contexts,
            Waits,
            Streams,
            Gateway,
            schedules,
            new TInvestLateCommissions(Attempts, contexts, reads, Streams, Updates, Clock, NullLogger<TInvestLateCommissions>.Instance),
            Updates,
            options,
            Clock,
            NullLogger<TInvestWaitingOrdersWorker>.Instance);
        Sweep = new TInvestClosedSessionSweep(
            Attempts,
            contexts,
            Streams,
            reads,
            Progress,
            schedules,
            options,
            Clock,
            NullLogger<TInvestClosedSessionSweep>.Instance);
    }

    public async Task<IReadOnlyList<ExternalOrderUpdate>> Next(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        List<ExternalOrderUpdate> read = [];

        await foreach (ExternalOrderUpdate update in Updates.Read(Bot, timeout.Token))
        {
            read.Add(update);

            if (read.Count == count)
            {
                break;
            }
        }

        return read;
    }

    public async Task<IReadOnlyList<ExternalOrderUpdate>> Until(Func<ExternalOrderUpdate, bool> last)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        List<ExternalOrderUpdate> read = [];

        await foreach (ExternalOrderUpdate update in Updates.Read(Bot, timeout.Token))
        {
            read.Add(update);

            if (last(update))
            {
                break;
            }
        }

        return read;
    }

    /// <summary>
    ///     What else the bot's updates receive within a short wait.
    /// </summary>
    public async Task<IReadOnlyList<ExternalOrderUpdate>> NextSoon()
    {
        using var wait = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        List<ExternalOrderUpdate> read = [];

        try
        {
            await foreach (ExternalOrderUpdate update in Updates.Read(Bot, wait.Token))
            {
                read.Add(update);
            }
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
            // Nothing else arrived.
        }

        return read;
    }

    /// <summary>
    ///     Moves the clock a second at a time until the condition holds, so the order state stream's pauses before it
    ///     subscribes again pass.
    /// </summary>
    public async Task AdvanceUntil(Func<bool> condition)
    {
        for (var step = 0; step < 400 && !condition(); step++)
        {
            Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        condition().ShouldBeTrue();
    }

    public static OrderSnapshot Limit(long id, OrderSide side, decimal quantity, decimal price)
    {
        return new OrderSnapshot(
            id,
            Tag: null,
            side,
            OrderType.Limit,
            quantity,
            price,
            QuoteBudget: null,
            Reservation: 0m,
            "node",
            EligibleFromCandleIndex: 0,
            OrderStatus.PendingSubmit);
    }

    public static OrderSnapshot MarketBuy(long id, decimal budget)
    {
        return new OrderSnapshot(
            id,
            Tag: null,
            OrderSide.Buy,
            OrderType.Market,
            Quantity: null,
            LimitPrice: null,
            budget,
            budget,
            "node",
            EligibleFromCandleIndex: 0,
            OrderStatus.PendingSubmit);
    }

    public static Task NoOp(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}