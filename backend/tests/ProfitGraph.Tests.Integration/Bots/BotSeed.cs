using System.Collections.Immutable;

using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Writes the rows a bot refers to (owner, strategy, version, credential) with invented values, and builds bots and
///     checkpoints for persistence tests.
/// </summary>
internal static class BotSeed
{
    /// <summary>
    ///     Invented encrypted values that no key decrypts.
    /// </summary>
    internal const string InventedSecrets = """
                                            {"apiKey":{"keyId":"test","nonce":"AAAAAAAAAAAAAAAA","ciphertext":"AQ==","tag":"AAAAAAAAAAAAAAAAAAAAAA=="},"apiSecret":{"keyId":"test","nonce":"AAAAAAAAAAAAAAAA","ciphertext":"AQ==","tag":"AAAAAAAAAAAAAAAAAAAAAA=="}}
                                            """;

    internal static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    internal static async Task<Bot> NewBot()
    {
        var owner = UserId.New();
        var strategy = StrategyId.New();
        var version = StrategyVersionId.New();
        var credential = ExchangeCredentialId.New();
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync(
            sql: """
                 INSERT INTO users (id, login, password_hash, role, status, created_at, updated_at)
                 VALUES (@owner, 'bot-owner-' || @owner, 'not-a-real-hash', 'User', 'Active', @now, @now);
                 INSERT INTO strategies (id, owner_id, name, current_version, created_at, updated_at) VALUES (@strategy, @owner, 'bot strategy', 1, @now, @now);
                 INSERT INTO strategy_versions (id, strategy_id, version_number, snapshot_json, created_at) VALUES (@version, @strategy, 1, '{}', @now);
                 INSERT INTO exchange_credentials (id, user_id, venue, label, api_key_last4, secrets, created_at)
                 VALUES (@credential, @owner, 'bybit', 'invented', 'abcd', CAST(@secrets AS jsonb), @now);
                 """,
            new
            {
                owner = owner.Value,
                strategy = strategy.Value,
                version = version.Value,
                credential = credential.Value,
                now = Now,
                secrets = InventedSecrets
            });

        return Bot.Create(
            BotId.New(),
            owner,
            strategy,
            version,
            credential,
            new CandleSeries(new VenueId("bybit"), new InstrumentSymbol("BTCUSDT"), Timeframe.Parse("1h")),
            new BotParameters(new Dictionary<string, decimal> { ["drop"] = 0.02m, ["rise"] = 0.015m }),
            allocationCap: 100m,
            new BotRiskLimits(maxOrderQuote: 20m, dailyLossLimitQuote: 10m),
            new BotFeeRates(limitRate: 0.001m, marketRate: 0.0015m, Now),
            Now);
    }

    internal static GraphEngineCheckpoint Checkpoint(long nextOrderId = 1, params OrderSnapshot[] orders)
    {
        var candle = new Candle(Now.AddHours(-1), Open: 100m, High: 101m, Low: 99m, Close: 100.5m, Volume: 3m);

        return new GraphEngineCheckpoint(
            new ExternalAccountCheckpoint(
                StartingCapital: 100m,
                QuoteTotal: 90m,
                QuoteReserved: 10m,
                BaseTotal: 0m,
                BaseReserved: 0m,
                CostBasis: 0m,
                RealizedPnl: 0m,
                FeesBase: 0m,
                FeesQuote: 0m,
                LastBuyPrice: null,
                nextOrderId,
                [.. orders],
                []),
            NextEventId: 4,
            NextAttemptId: 2,
            orders.ToImmutableDictionary(order => order.Id, _ => 1L),
            [],
            candle,
            LastClosedCandleIndex: 0,
            Now,
            Started: true,
            "v1",
            orders.ToImmutableDictionary(order => order.Id, _ => "v1"));
    }

    internal static OrderSnapshot MarketBuy(long id)
    {
        return new OrderSnapshot(
            id,
            "entry",
            OrderSide.Buy,
            OrderType.Market,
            Quantity: null,
            LimitPrice: null,
            QuoteBudget: 10m,
            Reservation: 10m,
            "entry",
            EligibleFromCandleIndex: 1,
            OrderStatus.PendingSubmit);
    }
}