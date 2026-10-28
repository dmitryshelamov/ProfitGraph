using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     The bot's private Bybit stream (ADR 0017, stage 5.8): opened by reconciliation, it turns order and execution
///     messages into looked-up updates, restores a lost connection and reads the bot's orders again (ADR 0044), and ends the
///     bot's updates on a foreign order, an unreadable message or a connection lost for the whole budget. Keys are invented
///     and nothing leaves the process.
/// </summary>
public sealed class BybitPrivateStreamTests
{
    private BybitGatewayHarness _harness = null!;

    [SetUp]
    public void Create()
    {
        _harness = new BybitGatewayHarness();
    }

    [Test]
    public void AuthIsSignedOverTheRealtimePathAndExpiry()
    {
        using JsonDocument auth =
            JsonDocument.Parse(BybitPrivateStreamMessages.Auth(MockBybitExchange.ApiKey, MockBybitExchange.ApiSecret, expiresAt: 1_793_000_010_000));
        JsonElement args = auth.RootElement.GetProperty("args");
        string expected = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(MockBybitExchange.ApiSecret), Encoding.UTF8.GetBytes("GET/realtime1793000010000")));

        auth.RootElement.GetProperty("op").GetString().ShouldBe("auth");
        args[0].GetString().ShouldBe(MockBybitExchange.ApiKey);
        args[1].GetInt64().ShouldBe(1_793_000_010_000);
        args[2].GetString().ShouldBe(expected);
    }

    [Test]
    public async Task ReconcileOpensTheStreamWithTheBotsCredentialBeforeLookingUp()
    {
        await Reconcile(allowForeign: false);

        _harness.PrivateStream.Connects.ShouldBe([(MockBybitExchange.ApiKey, MockBybitExchange.ServerTime + 10_000)]);
    }

    [Test]
    public async Task MessageNamingTheBotsOrderPublishesItsLookedUpState()
    {
        string clientOrderId = BybitClientOrderIds.For(_harness.Bot.Id, engineOrderId: 4).Value;
        await Reconcile(allowForeign: false);
        _harness.Exchange.AddOrder(clientOrderId, "Buy", "Filled", cumulativeBase: 0.1m, cumulativeQuote: 10m, new Dictionary<string, decimal> { ["BTC"] = 0.0001m });

        _harness.PrivateStream.Push(Message("order.spot", clientOrderId, "BTCUSDT"));
        _harness.PrivateStream.Push(Message("execution.spot", "pf-another-symbol", "ETHUSDT"));

        List<ExternalOrderUpdate> updates = await Read(count: 2);
        updates.ShouldBe([ExternalOrderUpdate.Accept(orderId: 4), ExternalOrderUpdate.Final(orderId: 4, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m)]);
    }

    [Test]
    public async Task ForeignOrderOnTheSymbolEndsTheUpdatesWithASafetyReason()
    {
        await Reconcile(allowForeign: false);

        _harness.PrivateStream.Push(Message("order.spot", "pf-manual-order", "BTCUSDT"));

        var failure = await Should.ThrowAsync<OrderGatewaySafetyException>(() => Read(count: 1));
        failure.Reason.ShouldBe(BotStatusReason.ForeignOrder);
    }

    [Test]
    public async Task UnreadableMessageEndsTheUpdates()
    {
        await Reconcile(allowForeign: false);

        _harness.PrivateStream.Push("not a message");

        await Should.ThrowAsync<JsonException>(() => Read(count: 1));
    }

    [Test]
    public async Task LostStreamConnectsAgainAndPublishesAFillItMissedOnce()
    {
        string clientOrderId = BybitClientOrderIds.For(_harness.Bot.Id, engineOrderId: 4).Value;
        _harness.Exchange.AddOrder(clientOrderId, "Buy", "New", cumulativeBase: 0m, cumulativeQuote: 0m, []);
        _harness.OrdersToRefresh.Add(new ReconcileOrderInternal(EngineOrderId: 4, new ClientOrderId(clientOrderId), MayHaveReachedVenue: true, AcceptedByEngine: true));
        await Reconcile(allowForeign: false);

        _harness.PrivateStream.Drop();
        _harness.Exchange.AddOrder(clientOrderId, "Buy", "Filled", cumulativeBase: 0.1m, cumulativeQuote: 10m, new Dictionary<string, decimal> { ["BTC"] = 0.0001m });
        await _harness.AdvanceUntil(() => _harness.PrivateStream.Connects.Count == 2);

        (await _harness.Published(count: 2)).ShouldBe(
            [ExternalOrderUpdate.Accept(orderId: 4), ExternalOrderUpdate.Final(orderId: 4, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m)]);
        (await _harness.PublishedSoon()).ShouldBeEmpty();
        _harness.PrivateStream.Connects[1].ShouldBe((MockBybitExchange.ApiKey, MockBybitExchange.ServerTime + 10_000));
    }

    [Test]
    public async Task StreamThatStaysLostForTheWholeBudgetEndsTheUpdatesSoTheBotReconcilesAgain()
    {
        await Reconcile(allowForeign: false);

        _harness.PrivateStream.Refuse = true;
        _harness.PrivateStream.Drop();
        Task<List<ExternalOrderUpdate>> reading = Read(count: 1);
        await _harness.AdvanceUntil(() => reading.IsCompleted);

        await Should.ThrowAsync<InvalidOperationException>(() => reading);
        _harness.PrivateStream.Connects.Count.ShouldBe(1);
        _harness.PrivateStream.CurrentIsDisposed.ShouldBeTrue();
    }

    [Test]
    public async Task ForeignOrderPlacedWhileTheStreamWasLostEndsTheUpdatesWithASafetyReason()
    {
        await Reconcile(allowForeign: false);

        _harness.PrivateStream.Drop();
        _harness.Exchange.AddOrder("pf-manual-order", "Buy", "New", cumulativeBase: 0m, cumulativeQuote: 0m, []);
        Task<List<ExternalOrderUpdate>> reading = Read(count: 1);
        await _harness.AdvanceUntil(() => reading.IsCompleted);

        (await Should.ThrowAsync<OrderGatewaySafetyException>(() => reading)).Reason.ShouldBe(BotStatusReason.ForeignOrder);
        _harness.PrivateStream.Connects.Count.ShouldBe(2);
    }

    [Test]
    public async Task RefusedStreamLeavesTheVenueUnavailable()
    {
        _harness.PrivateStream.Refuse = true;

        await Should.ThrowAsync<InvalidOperationException>(() => Reconcile(allowForeign: false));
    }

    [Test]
    public async Task DegradedReconciliationClosesTheStream()
    {
        _harness.Exchange.AddOrder("pf-manual-order", "Buy", "New", cumulativeBase: 0m, cumulativeQuote: 0m, []);

        (await Reconcile(allowForeign: false)).Degradation.ShouldBe(BotStatusReason.ForeignOrder);

        _harness.PrivateStream.CurrentIsDisposed.ShouldBeTrue();
    }

    private Task<BotReconciliationInternal> Reconcile(bool allowForeign)
    {
        return _harness.Gateway.Reconcile(new BotReconcileInternal(_harness.Bot.Id, allowForeign, []), CancellationToken.None);
    }

    private async Task<List<ExternalOrderUpdate>> Read(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = new List<ExternalOrderUpdate>();

        await foreach (ExternalOrderUpdate update in _harness.Gateway.Updates(_harness.Bot.Id, timeout.Token))
        {
            read.Add(update);

            if (read.Count == count)
            {
                break;
            }
        }

        return read;
    }

    private static string Message(string topic, string clientOrderId, string symbol)
    {
        return JsonSerializer.Serialize(new { topic, data = new[] { new { orderLinkId = clientOrderId, symbol, category = "spot" } } });
    }
}