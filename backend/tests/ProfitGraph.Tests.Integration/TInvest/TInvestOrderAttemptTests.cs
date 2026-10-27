using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Venues.TInvest.Persistence.Repositories;
using ProfitGraph.Venues.TInvest.Trading;

namespace ProfitGraph.Tests.Integration.TInvest;

/// <summary>
///     The T-Invest attempts table in PostgreSQL (ADR 0023): one row per attempt of an engine order under its request ID,
///     changes that apply only from the state they expect, the reason a planned attempt waits, and the owners of request
///     IDs for the foreign-order check.
/// </summary>
public sealed class TInvestOrderAttemptTests
{
    private ProfitGraphApiFactory _api = null!;
    private IHost _host = null!;
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
        _host = BotTestHost.Create();
        _dataSource = NpgsqlDataSource.Create(TestDatabase.ConnectionString);
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _dataSource.DisposeAsync();
        _host.Dispose();
        await _api.DisposeAsync();
    }

    [Test]
    public async Task AnAttemptIsRecordedOnce_AndMovesOnlyFromTheStateItExpects()
    {
        Bot bot = await AddBot();
        var attempts = new TInvestOrderAttemptRepository(_dataSource);
        TInvestOrderAttempt planned = Attempt(bot, engineOrderId: 1, attempt: 1, OrderType.Limit, price: 99.5m);

        (await attempts.Add(planned, CancellationToken.None)).ShouldBeTrue();
        (await attempts.Add(planned with { RequestId = Guid.NewGuid() }, CancellationToken.None)).ShouldBeFalse();
        (await attempts.Latest(bot.Id.Value, engineOrderId: 1, CancellationToken.None)).ShouldBe(planned);

        TInvestOrderAttempt sent = planned with { State = TInvestOrderAttemptState.Sent, UpdatedAt = BotSeed.Now.AddSeconds(1) };
        (await attempts.Transition(sent, TInvestOrderAttemptState.Planned, CancellationToken.None)).ShouldBeTrue();
        (await attempts.Transition(sent, TInvestOrderAttemptState.Planned, CancellationToken.None)).ShouldBeFalse();

        TInvestOrderAttempt final = sent with { State = TInvestOrderAttemptState.Final, BrokerCode = 30042, UpdatedAt = BotSeed.Now.AddSeconds(2) };
        (await attempts.Transition(final, TInvestOrderAttemptState.Sent, CancellationToken.None)).ShouldBeTrue();
        (await attempts.ByRequestId(planned.RequestId, CancellationToken.None)).ShouldBe(final);

        TInvestOrderAttempt second = Attempt(bot, engineOrderId: 1, attempt: 2, OrderType.Market, price: null);
        (await attempts.Add(second, CancellationToken.None)).ShouldBeTrue();
        (await attempts.Latest(bot.Id.Value, engineOrderId: 1, CancellationToken.None)).ShouldBe(second);
    }

    [Test]
    public async Task OwnersAreKnownOnlyForRecordedRequestIds()
    {
        Bot bot = await AddBot();
        var attempts = new TInvestOrderAttemptRepository(_dataSource);
        TInvestOrderAttempt attempt = Attempt(bot, engineOrderId: 3, attempt: 1, OrderType.Market, price: null);
        await attempts.Add(attempt, CancellationToken.None);
        var unknown = Guid.NewGuid();

        IReadOnlyDictionary<Guid, Guid> owners = await attempts.Owners([attempt.RequestId, unknown], CancellationToken.None);

        owners.ShouldBe(new Dictionary<Guid, Guid> { [attempt.RequestId] = bot.Id.Value });
        (await attempts.Owners([], CancellationToken.None)).ShouldBeEmpty();
    }

    [Test]
    public async Task AWaitingAttemptKeepsItsReasonAndBand_UntilItIsSent()
    {
        Bot bot = await AddBot();
        var attempts = new TInvestOrderAttemptRepository(_dataSource);
        TInvestOrderAttempt planned = Attempt(bot, engineOrderId: 5, attempt: 1, OrderType.Limit, price: 80m);
        await attempts.Add(planned, CancellationToken.None);

        TInvestOrderAttempt waiting = planned with { WaitingCode = OrderWaitingCode.PriceBand, BandLow = 90m, BandHigh = 110m };
        (await attempts.Transition(waiting, TInvestOrderAttemptState.Planned, CancellationToken.None)).ShouldBeTrue();
        (await attempts.Waiting(CancellationToken.None)).ShouldContain(waiting);

        TInvestOrderAttempt sent = waiting with { State = TInvestOrderAttemptState.Sent, WaitingCode = null, BandLow = null, BandHigh = null };
        (await attempts.Transition(sent, TInvestOrderAttemptState.Planned, CancellationToken.None)).ShouldBeTrue();
        (await attempts.Waiting(CancellationToken.None)).ShouldNotContain(attempt => attempt.RequestId == planned.RequestId);
        (await attempts.ByRequestId(planned.RequestId, CancellationToken.None)).ShouldBe(sent);
    }

    [Test]
    public async Task ACarriedAttemptKeepsWhoCancelledIt_ItsExecution_AndTheDateItWasCarriedFrom()
    {
        Bot bot = await AddBot();
        var attempts = new TInvestOrderAttemptRepository(_dataSource);
        TInvestOrderAttempt first = Attempt(bot, engineOrderId: 7, attempt: 1, OrderType.Limit, price: 100m);
        await attempts.Add(first, CancellationToken.None);
        TInvestOrderAttempt active = first with { State = TInvestOrderAttemptState.Active, ExchangeOrderId = "7000001" };
        await attempts.Transition(active, TInvestOrderAttemptState.Planned, CancellationToken.None);
        (await attempts.Working(CancellationToken.None)).ShouldContain(active);

        var tuesday = new DateOnly(year: 2026, month: 10, day: 27);
        TInvestOrderAttempt carried = active with
        {
            State = TInvestOrderAttemptState.Final,
            CancelledBy = TInvestCancelReason.CarryOver,
            CarriedFrom = tuesday,
            ExecutedLots = 1,
            ExecutedQuote = 995m,
            ExecutedFee = 0.5m
        };
        (await attempts.Transition(carried, TInvestOrderAttemptState.Active, CancellationToken.None)).ShouldBeTrue();
        TInvestOrderAttempt remainder = Attempt(bot, engineOrderId: 7, attempt: 2, OrderType.Limit, price: 100m) with
        {
            Lots = 2,
            WaitingCode = OrderWaitingCode.CarryOver,
            CarriedFrom = tuesday
        };
        await attempts.Add(remainder, CancellationToken.None);

        (await attempts.ForOrder(bot.Id.Value, engineOrderId: 7, CancellationToken.None)).ShouldBe([carried, remainder]);
        (await attempts.Working(CancellationToken.None)).ShouldNotContain(attempt => attempt.BotId == bot.Id.Value);
    }

    [Test]
    public async Task AnAttemptTheExchangeEndedWithItsDayKeepsThatReasonAndItsExecution()
    {
        Bot bot = await AddBot();
        var attempts = new TInvestOrderAttemptRepository(_dataSource);
        TInvestOrderAttempt first = Attempt(bot, engineOrderId: 9, attempt: 1, OrderType.Limit, price: 100m);
        await attempts.Add(first, CancellationToken.None);
        TInvestOrderAttempt active = first with { State = TInvestOrderAttemptState.Active, ExchangeOrderId = "7000002" };
        await attempts.Transition(active, TInvestOrderAttemptState.Planned, CancellationToken.None);
        TInvestOrderAttempt expired = active with
        {
            State = TInvestOrderAttemptState.Final,
            CancelledBy = TInvestCancelReason.Expired,
            ExecutedLots = 1,
            ExecutedQuote = 995m,
            ExecutedFee = 0.5m
        };

        (await attempts.Transition(expired, TInvestOrderAttemptState.Active, CancellationToken.None)).ShouldBeTrue();

        (await attempts.ByRequestId(first.RequestId, CancellationToken.None)).ShouldBe(expired);
        (await attempts.Working(CancellationToken.None)).ShouldNotContain(attempt => attempt.RequestId == first.RequestId);
    }

    [Test]
    public async Task AFinalAttemptWithoutItsCommissionIsPendingWithinTheWindow()
    {
        Bot bot = await AddBot();
        var attempts = new TInvestOrderAttemptRepository(_dataSource);
        TInvestOrderAttempt planned = Attempt(bot, engineOrderId: 9, attempt: 1, OrderType.Market, price: null);
        await attempts.Add(planned, CancellationToken.None);
        TInvestOrderAttempt filled = planned with { State = TInvestOrderAttemptState.Final, ExecutedLots = 3, ExecutedQuote = 300m, UpdatedAt = BotSeed.Now };
        await attempts.Transition(filled, TInvestOrderAttemptState.Planned, CancellationToken.None);

        (await attempts.PendingFees(BotSeed.Now.AddDays(-1), CancellationToken.None)).ShouldContain(filled);
        (await attempts.PendingFees(BotSeed.Now.AddSeconds(1), CancellationToken.None)).ShouldNotContain(attempt => attempt.RequestId == planned.RequestId);

        TInvestOrderAttempt charged = filled with { ExecutedFee = 0.9m };
        (await attempts.Transition(charged, TInvestOrderAttemptState.Final, CancellationToken.None)).ShouldBeTrue();
        (await attempts.PendingFees(BotSeed.Now.AddDays(-1), CancellationToken.None)).ShouldNotContain(attempt => attempt.RequestId == planned.RequestId);
    }

    [Test]
    public async Task OnlyTheBotsAttemptsOnTheShareThatExecutedAndChangedInTheWindowAreListed()
    {
        Bot bot = await AddBot();
        var attempts = new TInvestOrderAttemptRepository(_dataSource);
        TInvestOrderAttempt filled = Attempt(bot, engineOrderId: 9, attempt: 1, OrderType.Market, price: null);
        TInvestOrderAttempt unfilled = Attempt(bot, engineOrderId: 10, attempt: 1, OrderType.Market, price: null);
        await attempts.Add(filled, CancellationToken.None);
        await attempts.Add(unfilled, CancellationToken.None);
        TInvestOrderAttempt executed = filled with { State = TInvestOrderAttemptState.Final, ExecutedLots = 2, ExecutedQuote = 200m, UpdatedAt = BotSeed.Now };
        await attempts.Transition(executed, TInvestOrderAttemptState.Planned, CancellationToken.None);

        (await attempts.ExecutedOnShare(bot.Id.Value, filled.InstrumentUid, BotSeed.Now.AddMinutes(-5), CancellationToken.None)).ShouldBe([executed]);
        (await attempts.ExecutedOnShare(bot.Id.Value, filled.InstrumentUid, BotSeed.Now.AddSeconds(1), CancellationToken.None)).ShouldBeEmpty();
        (await attempts.ExecutedOnShare(bot.Id.Value, "another-share", BotSeed.Now.AddMinutes(-5), CancellationToken.None)).ShouldBeEmpty();
        (await attempts.ExecutedOnShare(Guid.NewGuid(), filled.InstrumentUid, BotSeed.Now.AddMinutes(-5), CancellationToken.None)).ShouldBeEmpty();
    }

    private static TInvestOrderAttempt Attempt(Bot bot, long engineOrderId, int attempt, OrderType type, decimal? price)
    {
        return new TInvestOrderAttempt(
            bot.Id.Value,
            engineOrderId,
            attempt,
            TInvestOrderRequestIds.For(bot.Id, engineOrderId, attempt),
            "e6123145-9665-43e0-8413-cd61b8aa9b13",
            OrderSide.Buy,
            type,
            Lots: 3,
            price,
            TInvestOrderAttemptState.Planned,
            ExchangeOrderId: null,
            BrokerCode: null,
            BotSeed.Now,
            BotSeed.Now);
    }

    private async Task<Bot> AddBot()
    {
        Bot bot = await BotSeed.NewBot();
        using IServiceScope scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);

        return bot;
    }
}