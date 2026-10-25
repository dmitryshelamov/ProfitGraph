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
///     changes that apply only from the state they expect, and the owners of request IDs for the foreign-order check.
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