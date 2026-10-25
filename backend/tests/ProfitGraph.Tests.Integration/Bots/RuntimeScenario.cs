using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Application.Strategies.CreateStrategy;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Infrastructure.Bots.Runtime;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Runs Ladder bots on the test venue in Worker hosts with the live role: scripted gateway, pushed live candles, flat
///     history and fast leases. Nothing leaves the process.
/// </summary>
internal sealed class RuntimeScenario
{
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(5);

    public ScriptedOrderGateway Gateway { get; } = new ScriptedOrderGateway();
    public FakeLiveCandleSource Candles { get; } = new FakeLiveCandleSource();

    /// <summary>
    ///     A Worker host with the live role; without a gateway it models a process where trading is disabled.
    /// </summary>
    public IHost Worker(bool withGateway = true)
    {
        return BotTestHost.Create(
            services =>
            {
                services.AddBotRuntime();
                services.AddSingleton<IInstrumentRulesSource, FakeInstrumentSource>();
                services.AddSingleton<IHistoricalCandleSource, FlatHistorySource>();
                services.AddSingleton<ILiveCandleSource>(Candles);

                if (withGateway)
                {
                    services.AddSingleton<IOrderGateway>(Gateway);
                }
            },
            settings: Settings());
    }

    /// <summary>
    ///     A Ladder bot that should run: its start buys at market once.
    /// </summary>
    public static async Task<Bot> RunningLadder(IHost host)
    {
        var owner = UserId.New();
        var credential = ExchangeCredentialId.New();

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await connection.ExecuteAsync(
                sql: """
                     INSERT INTO users (id, login, password_hash, role, status, created_at, updated_at)
                     VALUES (@owner, 'runtime-' || @owner, 'not-a-real-hash', 'User', 'Active', now(), now());
                     INSERT INTO exchange_credentials (
                         id, user_id, venue, label, api_key_last4, api_key_key_id, api_key_nonce, api_key_ciphertext, api_key_tag,
                         api_secret_key_id, api_secret_nonce, api_secret_ciphertext, api_secret_tag, created_at)
                     VALUES (@credential, @owner, 'test-history', 'invented', 'abcd', 'test', @nonce, @cipher, @tag, 'test', @nonce, @cipher, @tag, now());
                     """,
                new { owner = owner.Value, credential = credential.Value, nonce = new byte[12], cipher = new byte[] { 1 }, tag = new byte[16] });
        }

        StrategyDefinition definition = LadderStrategyBlockFactory.Create(buyAmount: 10m, allocationCap: 100m, drop: 0.05m, rise: 0.05m);
        using IServiceScope scope = host.Services.CreateScope();
        StrategyInternal strategy = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(
                new CreateStrategyCommand(
                    owner.Value,
                    "Runtime ladder",
                    definition.ToInternal(),
                    new GraphLayoutInternal(new Dictionary<string, NodePositionInternal>())));
        StrategyVersion version = (await scope.ServiceProvider.GetRequiredService<IStrategyRepository>()
            .GetVersion(new StrategyId(strategy.Id), new StrategyVersionNumber(strategy.CurrentVersion), CancellationToken.None))!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var bot = Bot.Create(
            BotId.New(),
            owner,
            new StrategyId(strategy.Id),
            version.Id,
            credential,
            new CandleSeries(new VenueId(TestHistoryVenue.WithHistory), new InstrumentSymbol("BTCUSDT"), Timeframe.Parse("1h")),
            new BotParameters(definition.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.DefaultValue)),
            allocationCap: 100m,
            new BotRiskLimits(maxOrderQuote: 20m, dailyLossLimitQuote: 10m),
            new BotFeeRates(limitRate: 0.001m, marketRate: 0.001m, now),
            now);
        bot.Start(version.Id, hasCheckpoint: false, tradingHalted: false, now);
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

    /// <summary>
    ///     Stops every bot of this run's database, so a runtime host in the next scenario runs only that scenario's bots.
    /// </summary>
    public static async Task StopEveryBot()
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync("UPDATE bots SET desired_state = 'Stopped' WHERE desired_state = 'Running'");
    }

    public static async Task<Bot> Read(IHost host, BotId id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IBotRepository>().Get(id, CancellationToken.None))!;
    }

    public static async Task<BotCheckpoint?> Checkpoint(IHost host, BotId id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IBotCheckpointRepository>().Get(id, CancellationToken.None);
    }

    public static async Task<IReadOnlyList<OrderIntent>> Intents(IHost host, BotId id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOrderIntentRepository>().ListByBot(id, CancellationToken.None);
    }

    public static async Task<(Guid Holder, long Epoch, DateTime ExpiresAt)?> Lease(BotId id)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.QuerySingleOrDefaultAsync<(Guid, long, DateTime)?>(
            "SELECT holder_id, epoch, expires_at FROM bot_leases WHERE bot_id = @id",
            new { id = id.Value });
    }

    public static async Task Until(Func<Task<bool>> condition, string what)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);

        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting until {what}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private static Dictionary<string, string?> Settings()
    {
        return new Dictionary<string, string?>
        {
            ["Bots:Runtime:LeaseDuration"] = LeaseDuration.ToString(),
            ["Bots:Runtime:RenewInterval"] = TimeSpan.FromMilliseconds(500).ToString(),
            ["Bots:Runtime:PollingInterval"] = TimeSpan.FromMilliseconds(100).ToString(),
            ["Bots:Runtime:DispatchInterval"] = TimeSpan.FromMilliseconds(50).ToString(),
            ["Bots:Runtime:ReconcileRetryDelay"] = TimeSpan.FromMilliseconds(100).ToString()
        };
    }
}