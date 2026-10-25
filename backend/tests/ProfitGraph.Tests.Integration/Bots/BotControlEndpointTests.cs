using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api.Accounts.Contracts;
using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Notifications.Contracts;
using ProfitGraph.Api.Strategies.Contracts;
using ProfitGraph.Api.Trading.Contracts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.RecordAccountBalance;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The bot kill switch, the global halt and alerts over HTTP (stage 5.9, ADR 0018), on the test venue with invented
///     credentials. It has its own API, so its sign-ins do not share the other bot tests' rate limit.
/// </summary>
[NonParallelizable]
public sealed class BotControlEndpointTests
{
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private ProfitGraphApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ProfitGraphApiFactory(BotApi.Settings());
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task KillStopsTheBotUntilItIsStartedAgain()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);

        JsonElement killed = await BotApi.Json(await owner.Client.PostAsync($"{BotApi.Path}/{id}/kill", content: null));
        JsonElement restarted = await BotApi.Json(await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null));

        killed.GetProperty("desiredState").GetString().ShouldBe("Stopped");
        killed.GetProperty("statusReason").GetString().ShouldBe("Killed");
        restarted.GetProperty("desiredState").GetString().ShouldBe("Running");
        restarted.GetProperty("statusReason").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task OnlyTheOwnerHaltsAndTheHaltKillsRunningBots()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);
        HttpClient admin = await _factory.SignIn(await _factory.CreateAccount(UserRole.Owner));

        (await owner.Client.PostAsync("/api/trading-halt/halt", content: null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        try
        {
            DateTimeOffset before = DateTimeOffset.UtcNow;
            var halted = (await (await admin.PostAsync("/api/trading-halt/halt", content: null)).Content.ReadFromJsonAsync<TradingHaltResponse>())!;
            JsonElement seen = await BotApi.Json(await owner.Client.GetAsync("/api/trading-halt"));
            JsonElement bot = await BotApi.Json(await owner.Client.GetAsync($"{BotApi.Path}/{id}"));

            halted.IsHalted.ShouldBeTrue();
            halted.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
            seen.GetProperty("isHalted").GetBoolean().ShouldBeTrue();
            bot.GetProperty("statusReason").GetString().ShouldBe("Killed");
            bot.GetProperty("desiredState").GetString().ShouldBe("Stopped");
        }
        finally
        {
            JsonElement resumed = await BotApi.Json(await admin.PostAsync("/api/trading-halt/resume", content: null));
            resumed.GetProperty("isHalted").GetBoolean().ShouldBeFalse();
        }
    }

    [Test]
    public async Task AlertsArePrivateToTheBotsOwnerAndAcknowledgedOnce()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        BotOwner other = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        Guid alert = await RaiseAlert(id, BotAlertKind.Degraded, nameof(BotStatusReason.ForeignOrder));
        HttpClient admin = await _factory.SignIn(await _factory.CreateAccount(UserRole.Owner));

        BotAlertResponse[] listed = (await owner.Client.GetFromJsonAsync<BotAlertResponse[]>("/api/alerts?unacknowledgedOnly=true", WebJson))!;
        BotAlertResponse[] ofBot = (await owner.Client.GetFromJsonAsync<BotAlertResponse[]>($"{BotApi.Path}/{id}/alerts", WebJson))!;
        BotAlertResponse[] ofOther = (await other.Client.GetFromJsonAsync<BotAlertResponse[]>("/api/alerts", WebJson))!;
        BotAlertResponse[] everyUser = (await admin.GetFromJsonAsync<BotAlertResponse[]>("/api/alerts?everyUser=true&limit=500", WebJson))!;
        HttpResponseMessage foreign = await other.Client.PostAsync($"/api/alerts/{alert}/acknowledge", content: null);
        HttpResponseMessage foreignBot = await other.Client.GetAsync($"{BotApi.Path}/{id}/alerts");
        var acknowledged =
            (await (await owner.Client.PostAsync($"/api/alerts/{alert}/acknowledge", content: null)).Content.ReadFromJsonAsync<BotAlertResponse>(WebJson))!;
        BotAlertResponse[] unread = (await owner.Client.GetFromJsonAsync<BotAlertResponse[]>("/api/alerts?unacknowledgedOnly=true", WebJson))!;

        listed.ShouldBe([new BotAlertResponse(alert, id, BotAlertKind.Degraded, "ForeignOrder", BotSeed.Now, AcknowledgedAt: null)]);
        ofBot.Select(item => item.Id).ShouldBe([alert]);
        ofOther.ShouldBeEmpty();
        everyUser.Select(item => item.Id).ShouldContain(alert);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreignBot.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        acknowledged.AcknowledgedAt.ShouldNotBeNull();
        acknowledged.BotId.ShouldBe(id);
        acknowledged.Kind.ShouldBe(BotAlertKind.Degraded);
        acknowledged.Reason.ShouldBe("ForeignOrder");
        acknowledged.CreatedAt.ShouldBe(BotSeed.Now);
        unread.ShouldBeEmpty();
    }

    [Test]
    public async Task SavingAStrategyOfARunningBotNeedsTheLiveImpactConfirmed()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        JsonElement started = await BotApi.Json(await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null));
        JsonElement strategy = await BotApi.Json(await owner.Client.GetAsync($"/api/strategies/{owner.StrategyId}"));

        LiveImpactBotResponse[] impact = (await owner.Client.GetFromJsonAsync<LiveImpactBotResponse[]>($"/api/strategies/{owner.StrategyId}/live-impact", WebJson))!;
        HttpResponseMessage refused = await owner.Client.PutAsJsonAsync($"/api/strategies/{owner.StrategyId}", Save(strategy, confirm: false));
        HttpResponseMessage saved = await owner.Client.PutAsJsonAsync($"/api/strategies/{owner.StrategyId}", Save(strategy, confirm: true));

        LiveImpactBotResponse bot = impact.ShouldHaveSingleItem();
        bot.BotId.ShouldBe(id);
        bot.Venue.ShouldBe("test-history");
        bot.Symbol.ShouldBe("BTCUSDT");
        bot.Timeframe.ShouldBe("1h");
        bot.StrategyVersionId.ShouldBe(started.GetProperty("strategyVersionId").GetGuid());
        bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Created);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(refused)).ShouldBe("live_impact");
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task ManualOrderWaitsForARunningBotAndIsReadBack()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);

        HttpResponseMessage notRunning = await owner.Client.PostAsJsonAsync($"{BotApi.Path}/{id}/manual-orders", new { side = "Buy", amount = "10" });
        await MarkRunning(id);
        HttpResponseMessage accepted = await owner.Client.PostAsJsonAsync($"{BotApi.Path}/{id}/manual-orders", new { side = "Buy", amount = "10" });
        var submitted = (await accepted.Content.ReadFromJsonAsync<ManualOrderResponse>(WebJson))!;
        var read = (await owner.Client.GetFromJsonAsync<ManualOrderResponse>($"{BotApi.Path}/{id}/manual-orders/{submitted.Id}", WebJson))!;

        notRunning.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(notRunning)).ShouldBe(BotRuleException.NotRunning);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        read.Id.ShouldBe(submitted.Id);
        read.BotId.ShouldBe(id);
        read.Side.ShouldBe(OrderSide.Buy);
        read.Amount.ShouldBe("10");
        read.Status.ShouldBe(ManualOrderStatus.Pending);
        read.EngineOrderId.ShouldBeNull();
        read.RejectionReason.ShouldBeNull();

        // The stored time has microsecond precision; the accepted response carries the clock's ticks.
        read.CreatedAt.ShouldBe(submitted.CreatedAt, TimeSpan.FromMilliseconds(1));
        read.ResolvedAt.ShouldBeNull();
    }

    [Test]
    public async Task FillsArePagedNewestFirstAndThePositionShowsItsCycles()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        DateTimeOffset at = DateTimeOffset.UtcNow;
        await Record(
            id,
            BotFill.Record(
                BotFillId.New(),
                new BotId(id),
                engineOrderId: 1,
                OrderSide.Buy,
                "entry",
                grossBase: 0.1m,
                grossQuote: 10m,
                fee: 0.0001m,
                cumulativeGrossBase: 0.1m,
                at),
            BotFill.Record(
                BotFillId.New(),
                new BotId(id),
                engineOrderId: 2,
                OrderSide.Sell,
                "exit",
                grossBase: 0.05m,
                grossQuote: 6m,
                fee: 0.006m,
                cumulativeGrossBase: 0.05m,
                at));

        var first = (await owner.Client.GetFromJsonAsync<BotFillPageResponse>($"{BotApi.Path}/{id}/fills?limit=1", WebJson))!;
        var second = (await owner.Client.GetFromJsonAsync<BotFillPageResponse>($"{BotApi.Path}/{id}/fills?limit=1&before={first.Next}", WebJson))!;
        var position = (await owner.Client.GetFromJsonAsync<BotPositionResponse>($"{BotApi.Path}/{id}/position", WebJson))!;

        BotFillResponse sell = first.Items.ShouldHaveSingleItem();
        sell.EngineOrderId.ShouldBe(2);
        sell.Side.ShouldBe(OrderSide.Sell);
        sell.Tag.ShouldBe("exit");
        sell.GrossBase.ShouldBe("0.05");
        sell.GrossQuote.ShouldBe("6");
        sell.Fee.ShouldBe("0.006");
        sell.FeeAsset.ShouldBe(FeeAsset.Quote);
        sell.Price.ShouldBe("120");
        sell.FilledAt.ShouldBe(at, TimeSpan.FromMilliseconds(1));
        sell.Id.ShouldNotBe(Guid.Empty);
        second.Items.ShouldHaveSingleItem().Sequence.ShouldBeLessThan(sell.Sequence);
        second.Next.ShouldNotBeNull();
        position.BaseTotal.ShouldBe("0");
        position.QuoteTotal.ShouldBe("100");
        position.CostBasis.ShouldBe("0");
        position.AverageCost.ShouldBeNull();
        position.RealizedPnl.ShouldBe("0");
        position.OpenedAt.ShouldBeNull();
        position.MarkPrice.ShouldBeNull();
        position.MarkPriceObservedAt.ShouldBeNull();
        position.MarkPriceSource.ShouldBeNull();
        position.UnrealizedPnl.ShouldBeNull();
        BotPositionCycleResponse cycle = position.Cycles.ShouldHaveSingleItem();
        cycle.Id.ShouldNotBe(Guid.Empty);
        cycle.OpenedAt.ShouldBe(at, TimeSpan.FromMilliseconds(1));
        cycle.ClosedAt.ShouldNotBeNull();
        cycle.RealizedPnl.ShouldBe("1");
    }

    [Test]
    public async Task FeedIsPagedNewestFirst()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        DateTimeOffset at = DateTimeOffset.UtcNow;

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
                .ExecuteAsync(
                    async () =>
                    {
                        var feed = scope.ServiceProvider.GetRequiredService<IBotFeedRepository>();
                        await feed.Add(
                            BotFeedEntry.Record(BotFeedEntryId.New(), new BotId(id), BotFeedKind.Started, new Dictionary<string, string>(), at),
                            CancellationToken.None);
                        await feed.Add(
                            BotFeedEntry.Record(BotFeedEntryId.New(), new BotId(id), BotFeedKind.Killed, new Dictionary<string, string> { ["reason"] = "Killed" }, at),
                            CancellationToken.None);

                        return true;
                    },
                    CancellationToken.None);
        }

        var first = (await owner.Client.GetFromJsonAsync<BotFeedPageResponse>($"{BotApi.Path}/{id}/feed?limit=1", WebJson))!;
        var second = (await owner.Client.GetFromJsonAsync<BotFeedPageResponse>($"{BotApi.Path}/{id}/feed?limit=1&before={first.Next}", WebJson))!;

        BotFeedEntryResponse killed = first.Items.ShouldHaveSingleItem();
        killed.Kind.ShouldBe(BotFeedKind.Killed);
        killed.Details.ShouldBe(new Dictionary<string, string> { ["reason"] = "Killed" });
        killed.OccurredAt.ShouldBe(at, TimeSpan.FromMilliseconds(1));
        killed.Id.ShouldNotBe(Guid.Empty);
        BotFeedEntryResponse started = second.Items.ShouldHaveSingleItem();
        started.Kind.ShouldBe(BotFeedKind.Started);
        started.Sequence.ShouldBeLessThan(killed.Sequence);
    }

    [Test]
    public async Task AccountHistoryShowsSnapshotsAndHowReadingGoes()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        await BotApi.Create(owner);
        DateTimeOffset at = DateTimeOffset.UtcNow.AddMinutes(-1);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(
                    new RecordAccountBalanceCommand(
                        owner.CredentialId,
                        AccountBalanceReadInternal.Observed(
                            new AccountBalanceObservationInternal(
                                TotalValueUsd: 1234.5m,
                                AvailableValueUsd: 1000m,
                                [new AccountCoinBalanceInternal("BTC", Balance: 0.0035m, UsdValue: 234.5m)],
                                at))));
        }

        var history = (await owner.Client.GetFromJsonAsync<AccountHistoryResponse>($"/api/exchange-credentials/{owner.CredentialId}/account", WebJson))!;

        history.LastAttemptAt.ShouldNotBeNull();
        history.LastSuccessAt.ShouldNotBeNull();
        history.FailureReason.ShouldBeNull();
        history.NextAttemptAt.ShouldNotBeNull();
        AccountSnapshotResponse snapshot = history.Snapshots.ShouldHaveSingleItem();
        snapshot.CapturedAt.ShouldBe(at, TimeSpan.FromMilliseconds(1));
        snapshot.TotalValueUsd.ShouldBe("1234.5");
        snapshot.AvailableValueUsd.ShouldBe("1000");
        AccountCoinBalanceResponse coin = snapshot.Coins.ShouldHaveSingleItem();
        coin.Coin.ShouldBe("BTC");
        coin.Balance.ShouldBe("0.0035");
        coin.UsdValue.ShouldBe("234.5");
    }

    [Test]
    public async Task TelegramLinkCodeIsIssuedAndTheLinkCanBeRemoved()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);

        var code = (await (await owner.Client.PostAsync("/api/profile/telegram/link-code", content: null)).Content.ReadFromJsonAsync<TelegramLinkCodeResponse>(WebJson))!;
        var status = (await owner.Client.GetFromJsonAsync<TelegramLinkResponse>("/api/profile/telegram", WebJson))!;
        HttpResponseMessage unlinked = await owner.Client.DeleteAsync("/api/profile/telegram");

        code.Code.Length.ShouldBe(10);
        code.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(9));
        code.StartLink.ShouldBeNull();
        status.Enabled.ShouldBeFalse();
        status.Linked.ShouldBeFalse();
        status.LinkedAt.ShouldBeNull();
        status.Blocked.ShouldBeFalse();
        status.BotUsername.ShouldBeNull();
        unlinked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task Record(Guid botId, params BotFill[] fills)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    foreach (BotFill fill in fills)
                    {
                        await scope.ServiceProvider.GetRequiredService<IBotFillRepository>().TryAdd(fill, CancellationToken.None);
                    }

                    BotPositionCycle cycle = BotPositionCycle.Open(BotPositionCycleId.New(), new BotId(botId), fills[0].FilledAt, realizedPnl: 0m);
                    cycle.Close(fills[^1].FilledAt, realizedPnl: 1m);
                    await scope.ServiceProvider.GetRequiredService<IBotPositionCycleRepository>().Add(cycle, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }

    private async Task MarkRunning(Guid botId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var bots = scope.ServiceProvider.GetRequiredService<IBotRepository>();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    Bot bot = (await bots.GetForUpdate(new BotId(botId), CancellationToken.None))!;
                    bot.MarkRunning(DateTimeOffset.UtcNow);
                    await bots.Update(bot, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }

    private static object Save(JsonElement strategy, bool confirm)
    {
        return new
        {
            expectedVersion = strategy.GetProperty("currentVersion").GetInt32(),
            name = strategy.GetProperty("name").GetString(),
            definition = strategy.GetProperty("definition"),
            layout = strategy.GetProperty("layout"),
            confirmLiveImpact = confirm
        };
    }

    private async Task<Guid> RaiseAlert(Guid botId, BotAlertKind kind, string reason)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        Bot bot = (await scope.ServiceProvider.GetRequiredService<IBotRepository>().Get(new BotId(botId), CancellationToken.None))!;
        BotAlert alert = BotAlert.Raise(BotAlertId.New(), bot.Id, bot.OwnerId, kind, reason, BotSeed.Now);
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IBotAlertRepository>().Add(alert, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);

        return alert.Id.Value;
    }
}