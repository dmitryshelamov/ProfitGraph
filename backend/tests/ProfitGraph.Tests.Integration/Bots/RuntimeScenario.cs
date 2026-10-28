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
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.Feed;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Infrastructure.Bots.Runtime;
using ProfitGraph.Infrastructure.Common.Outbox;
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
    public ScriptedOrderReadiness Readiness { get; } = new ScriptedOrderReadiness();
    public FakeLiveCandleSource Candles { get; } = new FakeLiveCandleSource();
    public FakeLiveMarkPriceSource Prices { get; } = new FakeLiveMarkPriceSource();
    public FlatHistorySource History { get; } = new FlatHistorySource();
    public FakeInstrumentSource Instruments { get; } = new FakeInstrumentSource();

    /// <summary>
    ///     A Worker host with the live role; without a gateway it models a process where trading is disabled, and with the outbox its events reach the bot's feed.
    ///     <paramref name="configure" /> adds services of one test.
    /// </summary>
    public IHost Worker(bool withGateway = true, bool withOutbox = false, Action<IServiceCollection>? configure = null)
    {
        return BotTestHost.Create(
            services =>
            {
                services.AddBotRuntime();
                services.AddSingleton<IInstrumentRulesSource>(Instruments);
                services.AddSingleton<IHistoricalCandleSource>(History);
                services.AddSingleton<ILiveCandleSource>(Candles);
                services.AddSingleton<ILiveMarkPriceSource>(Prices);

                if (withOutbox)
                {
                    services.AddOutboxDispatcher();
                }

                if (withGateway)
                {
                    services.AddSingleton<IOrderGateway>(Gateway);
                    services.AddSingleton<IOrderReadiness>(Readiness);
                }

                configure?.Invoke(services);
            },
            settings: Settings());
    }

    /// <summary>
    ///     A Ladder bot that should run: its start buys at market once.
    /// </summary>
    public static async Task<Bot> RunningLadder(IHost host)
    {
        return (await RunningLadders(host, count: 1))[0];
    }

    /// <summary>
    ///     A Ladder bot whose strategy restores its missing orders when a session resumes (the template of ADR 0023's bots).
    /// </summary>
    public static async Task<Bot> RunningResumingLadder(IHost host)
    {
        return (await RunningLadders(host, count: 1, existingOwner: null, resuming: true))[0];
    }

    /// <summary>
    ///     Ladder bots of one owner on one strategy, each with its own invented credential, that should run. Without an
    ///     <paramref name="existingOwner" /> the owner is a new user who cannot sign in.
    /// </summary>
    public static async Task<IReadOnlyList<Bot>> RunningLadders(IHost host, int count, UserId? existingOwner = null, bool resuming = false)
    {
        UserId owner = existingOwner ?? UserId.New();
        ExchangeCredentialId[] credentials = [.. Enumerable.Range(start: 0, count).Select(_ => ExchangeCredentialId.New())];

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            if (existingOwner is null)
            {
                await connection.ExecuteAsync(
                    "INSERT INTO users (id, login, password_hash, role, status, created_at, updated_at) VALUES (@owner, 'runtime-' || @owner, 'not-a-real-hash', 'User', 'Active', now(), now())",
                    new { owner = owner.Value });
            }

            foreach (ExchangeCredentialId credential in credentials)
            {
                await connection.ExecuteAsync(
                    sql: """
                         INSERT INTO exchange_credentials (id, user_id, venue, label, api_key_last4, secrets, created_at)
                         VALUES (@credential, @owner, 'test-history', 'invented', 'abcd', CAST(@secrets AS jsonb), now());
                         """,
                    new { owner = owner.Value, credential = credential.Value, secrets = BotSeed.InventedSecrets });
            }
        }

        StrategyDefinition definition = resuming
            ? LadderResumeStrategyBlockFactory.Create(buyAmount: 10m, allocationCap: 100m, drop: 0.05m, rise: 0.05m)
            : LadderStrategyBlockFactory.Create(buyAmount: 10m, allocationCap: 100m, drop: 0.05m, rise: 0.05m);
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
        Bot[] bots =
        [
            .. credentials.Select(credential => Bot.Create(
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
                now))
        ];

        foreach (Bot bot in bots)
        {
            bot.Start(version.Id, hasCheckpoint: false, tradingHalted: false, now);
        }

        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    foreach (Bot bot in bots)
                    {
                        await scope.ServiceProvider.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);
                    }

                    return true;
                },
                CancellationToken.None);

        return bots;
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

    public static async Task<IReadOnlyList<BotFeedEntry>> Feed(IHost host, BotId id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        IReadOnlyList<SequencedBotFeedEntry> entries =
            await scope.ServiceProvider.GetRequiredService<IBotFeedRepository>().List(id, before: null, limit: 100, CancellationToken.None);

        return [.. entries.Select(entry => entry.Entry)];
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
                Assert.Fail($"Timed out waiting until {what}. {await DatabaseState()}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    /// <summary>
    ///     What may hold every bot back: the global halt, leases, the bots that should run, and sessions that wait for a lock
    ///     or sit in a transaction.
    /// </summary>
    private static async Task<string> DatabaseState()
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        var halted = await connection.ExecuteScalarAsync<bool>("SELECT halted FROM trading_halt WHERE id");
        IEnumerable<string> leases = await connection.QueryAsync<string>(
            """
            SELECT left(lease.holder_id::text, 8) || ' ' || bot.desired_state || ' ' || bot.runtime_status || ' until ' || (lease.expires_at - now())::text
            FROM bot_leases AS lease JOIN bots AS bot ON bot.id = lease.bot_id WHERE lease.expires_at > now()
            """);
        IEnumerable<string> sessions = await connection.QueryAsync<string>(
            """
            SELECT pid || ' ' || state || ' ' || coalesce(wait_event_type, '-') || ' for ' || coalesce((now() - xact_start)::text, '-') || ' blocked by '
                || pg_blocking_pids(pid)::text || ' ' || left(query, 160)
            FROM pg_stat_activity
            WHERE datname = current_database() AND pid <> pg_backend_pid() AND (wait_event_type = 'Lock' OR state LIKE 'idle in transaction%')
            """);

        IEnumerable<string> running = await connection.QueryAsync<string>(
            """
            SELECT runtime_status || ' ' || coalesce(status_reason, '-') || ' restarts ' || restart_attempts || ' next ' || coalesce(next_restart_at::text, '-')
            FROM bots WHERE desired_state = 'Running'
            """);

        IEnumerable<string> waiting = await connection.QueryAsync<string>(
            """
            SELECT intent.kind || ' ' || intent.status || ' attempts ' || intent.attempts || ' bot ' || bot.desired_state || ' venue ' || bot.venue
                || ' lease ' || coalesce(left(lease.holder_id::text, 8) || ' ttl ' || (lease.expires_at - now())::text, '-')
            FROM bot_order_intents AS intent JOIN bots AS bot ON bot.id = intent.bot_id LEFT JOIN bot_leases AS lease ON lease.bot_id = intent.bot_id
            WHERE intent.status IN ('Pending', 'Sending') ORDER BY intent.sequence LIMIT 12
            """);

        return
            $"Halted: {halted}; live leases: [{string.Join("; ", leases)}]; bots that should run: [{string.Join("; ", running)}]; waiting intents: [{string.Join("; ", waiting)}]; blocked or open sessions: [{string.Join("; ", sessions)}].";
    }

    private static Dictionary<string, string?> Settings()
    {
        return new Dictionary<string, string?>
        {
            ["Bots:Runtime:LeaseDuration"] = LeaseDuration.ToString(),
            ["Bots:Runtime:RenewInterval"] = TimeSpan.FromMilliseconds(500).ToString(),
            ["Bots:Runtime:PollingInterval"] = TimeSpan.FromMilliseconds(100).ToString(),
            ["Bots:Runtime:DispatchInterval"] = TimeSpan.FromMilliseconds(50).ToString(),
            ["Bots:Runtime:ReconcileRetryDelay"] = TimeSpan.FromMilliseconds(100).ToString(),
            ["Bots:Runtime:ManualOrderPollInterval"] = TimeSpan.FromMilliseconds(100).ToString(),
            ["Bots:Runtime:GraphCheckInterval"] = TimeSpan.FromMilliseconds(100).ToString()
        };
    }
}