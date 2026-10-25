using Dapper;

using MediatR;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotPosition;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Display prices of live bots (stage 5.13, S5-19): the Worker stores the newest observation of each running bot's
///     instrument, the position shows a fresh ticker instead of the candle, and the hub sends it only to the bot's owner.
/// </summary>
[NonParallelizable]
public sealed class LivePriceTests
{
    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
    }

    [SetUp]
    public Task Isolate()
    {
        return RuntimeScenario.StopEveryBot();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task OlderObservationNeverReplacesANewerOne()
    {
        using IServiceScope scope = _api.Services.CreateScope();
        var prices = scope.ServiceProvider.GetRequiredService<ILiveMarkPriceRepository>();
        var symbol = new InstrumentSymbol($"T{Guid.NewGuid():N}"[..12].ToUpperInvariant());
        var venue = new VenueId("bybit");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        bool first = await prices.Store(new LiveMarkPrice(venue, symbol, Price: 100m, now), CancellationToken.None);
        bool older = await prices.Store(new LiveMarkPrice(venue, symbol, Price: 90m, now.AddSeconds(-1)), CancellationToken.None);
        bool newer = await prices.Store(new LiveMarkPrice(venue, symbol, Price: 110m, now.AddSeconds(1)), CancellationToken.None);

        (first, older, newer).ShouldBe((true, false, true));
        (await prices.Get(venue, symbol, CancellationToken.None))!.Price.ShouldBe(110m);
    }

    [Test]
    public async Task WorkerStoresThePriceOfARunningBotsInstrument()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;

        await RuntimeScenario.Until(
            async () =>
            {
                scenario.Prices.Push(price: 123.45m, observedAt);

                return await Stored(bot) == 123.45m;
            },
            "the price is stored");
        await worker.StopAsync();
    }

    [Test]
    public async Task PositionShowsAFreshTickerInsteadOfTheCandle()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(async () => await RuntimeScenario.Checkpoint(worker, bot.Id) is not null, "the bot has a checkpoint");
        await worker.StopAsync();

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            // Other scenarios trade the same instrument of the test venue.
            await connection.ExecuteAsync(
                "DELETE FROM live_mark_prices WHERE venue = @venue AND symbol = @symbol",
                new { venue = bot.Series.Venue.Value, symbol = bot.Series.Symbol.Value });
        }

        BotPositionInternal fromCandle = await Position(worker, bot);

        using (IServiceScope scope = worker.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILiveMarkPriceRepository>()
                .Store(new LiveMarkPrice(bot.Series.Venue, bot.Series.Symbol, Price: 150m, DateTimeOffset.UtcNow), CancellationToken.None);
        }

        BotPositionInternal fromTicker = await Position(worker, bot);

        fromCandle.MarkPriceSource.ShouldBe(MarkPriceSource.Candle);
        fromCandle.MarkPrice.ShouldBe((await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.LastClosedCandle.Close);
        fromTicker.MarkPriceSource.ShouldBe(MarkPriceSource.Ticker);
        fromTicker.MarkPrice.ShouldBe(150m);
        fromTicker.UnrealizedPnl.ShouldBe(fromTicker.BaseTotal * 150m - fromTicker.CostBasis);
    }

    [Test]
    public async Task HubSendsAStoredPriceOnlyToTheBotsOwner()
    {
        using IHost host = BotTestHost.Create();
        UserInternal owner = await _api.CreateAccount(UserRole.User);
        UserInternal other = await _api.CreateAccount(UserRole.User);
        Bot bot = (await RuntimeScenario.RunningLadders(host, count: 1, new UserId(owner.Id)))[0];
        await using HubConnection ownerConnection = await Connect(owner);
        await using HubConnection otherConnection = await Connect(other);
        var received = new TaskCompletionSource<LivePriceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        ownerConnection.On<LivePriceResponse>("LivePrice", price => received.TrySetResult(price));

        await ownerConnection.InvokeAsync("Subscribe", bot.Id.Value);
        var refused = await Should.ThrowAsync<HubException>(() => otherConnection.InvokeAsync("Subscribe", bot.Id.Value));
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;

        using (IServiceScope scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILiveMarkPriceRepository>()
                .Store(new LiveMarkPrice(bot.Series.Venue, bot.Series.Symbol, Price: 99.5m, observedAt), CancellationToken.None);
        }

        LivePriceResponse price = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        refused.Message.ShouldContain("bot_not_found");
        price.Venue.ShouldBe(bot.Series.Venue.Value);
        price.Symbol.ShouldBe(bot.Series.Symbol.Value);
        price.Price.ShouldBe("99.5");
        price.ObservedAt.ShouldBe(observedAt, TimeSpan.FromMilliseconds(1));
    }

    private static async Task<decimal?> Stored(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<decimal?>(
            "SELECT price FROM live_mark_prices WHERE venue = @venue AND symbol = @symbol",
            new { venue = bot.Series.Venue.Value, symbol = bot.Series.Symbol.Value });
    }

    private static async Task<BotPositionInternal> Position(IHost host, Bot bot)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetBotPositionQuery(bot.OwnerId.Value, bot.Id.Value, CycleLimit: 5));
    }

    private async Task<HubConnection> Connect(UserInternal user)
    {
        string cookie = await _api.SessionCookie(user);
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_api.Server.BaseAddress, "/ws/live-prices"),
                options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => _api.Server.CreateHandler();
                    options.Headers["Cookie"] = cookie;
                })
            .Build();
        await connection.StartAsync();

        return connection;
    }
}