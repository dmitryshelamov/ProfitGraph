using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Orders;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     A Steam sell waits while every item is under its trade hold and goes as soon as one is tradable (ADR 0024).
/// </summary>
public sealed class SteamOrderReadinessTests
{
    private const string Item = "Dreams & Nightmares Case";

    private static readonly Guid Bot = Guid.NewGuid();
    private static readonly SellerFeeSchedule Fees = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m);
    private static readonly DateTimeOffset Unlock = new DateTimeOffset(year: 2026, month: 11, day: 2, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private SteamSessionHarness _harness = null!;
    private InMemorySteamOrderJournal _journal = null!;
    private FakeSteamOwnedAssets _owned = null!;
    private SteamOrderReadiness _readiness = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SteamSessionHarness
        {
            Options = new SteamOptions
            {
                Enabled = true,
                CommunityBaseAddress = MockSteam.Community,
                LoginBaseAddress = MockSteam.Login,
                ApiBaseAddress = MockSteam.Api,
                WriteSpacing = TimeSpan.Zero
            }
        };
        _harness.Clock.SetUtcNow(Unlock.AddDays(-7));
        _journal = new InMemorySteamOrderJournal();
        _owned = new FakeSteamOwnedAssets();
        var updates = new SteamOrderUpdates();
        var waiting = new RecordingSteamOrderWaiting();
        var sells = new SteamSells(
            _journal,
            _owned,
            _harness.Sessions(),
            new SteamListingClient(Options.Create(_harness.Options)),
            new SteamSellSettlement(_journal, updates, waiting, _harness.Clock),
            updates,
            waiting,
            _harness.Clock);
        _readiness = new SteamOrderReadiness(new FakeSteamBotAccounts(Account), _journal, sells, _harness.Clock);
    }

    [Test]
    public async Task ASellWhoseItemsAreAllUnderTheirHoldWaitsUntilTheEarliestUnlock()
    {
        _owned.Assets.AddRange([Asset("1001", Unlock), Asset("1002", Unlock.AddDays(1))]);

        OrderReadinessInternal answer = await _readiness.Evaluate(Submit(Sell(quantity: 2m)), CancellationToken.None);

        answer.ShouldSatisfyAllConditions(
            () => answer.Code.ShouldBe(OrderWaitingCode.SettlementHold),
            () => answer.Until.ShouldBe(Unlock),
            () => answer.RecheckAt.ShouldBe(Unlock));
    }

    [Test]
    public async Task ASellWithATradableItemIsReady()
    {
        _owned.Assets.AddRange([Asset("1001", DateTimeOffset.UnixEpoch), Asset("1002", Unlock)]);

        (await _readiness.Evaluate(Submit(Sell(quantity: 2m)), CancellationToken.None)).IsReady.ShouldBeTrue();
    }

    [Test]
    public async Task ASellItCannotCoverAJournaledSellAndABuyAreLeftToTheModule()
    {
        _owned.Assets.Add(Asset("1001", Unlock));

        (await _readiness.Evaluate(Submit(Sell(quantity: 2m)), CancellationToken.None)).IsReady.ShouldBeTrue();
        (await _readiness.Evaluate(Submit(Sell(quantity: 1m) with { Side = OrderSide.Buy }), CancellationToken.None)).IsReady.ShouldBeTrue();

        OrderSubmitInternal journaled = Submit(Sell(quantity: 1m));
        (await _readiness.Evaluate(journaled, CancellationToken.None)).IsReady.ShouldBeFalse();
        await _journal.AddSell(
            new SteamOrderJournalEntry(
                journaled.ClientOrderId.Value,
                Bot,
                SteamSessionHarness.Credential,
                EngineOrderId: 1,
                $"KZT:730:{Item}",
                OrderSide.Sell,
                OrderType.Limit,
                Quantity: 1,
                PriceMinorUnits: 100,
                SteamOrderJournalState.Waiting,
                WaitingCode: null,
                SteamOrderId: null,
                ConfirmationCreator: null,
                Outcome: null,
                PlaceStartedAt: null,
                Deadline: null,
                _harness.Clock.GetUtcNow(),
                _harness.Clock.GetUtcNow()),
            [],
            CancellationToken.None);
        (await _readiness.Evaluate(journaled, CancellationToken.None)).IsReady.ShouldBeTrue();
    }

    private static SteamOwnedAsset Asset(string id, DateTimeOffset unlockAt)
    {
        return new SteamOwnedAsset(AppId: 730, "2", id, unlockAt);
    }

    private static SteamBotAccount Account()
    {
        SteamInstrument.TryParse($"KZT:730:{Item}", out SteamInstrument? instrument).ShouldBeTrue();

        return new SteamBotAccount(Bot, SteamSessionHarness.Owner, SteamSessionHarness.Credential, instrument, Fees);
    }

    private static OrderSnapshot Sell(decimal quantity)
    {
        return new OrderSnapshot(
            Id: 1,
            Tag: null,
            OrderSide.Sell,
            OrderType.Limit,
            quantity,
            LimitPrice: 100m,
            QuoteBudget: null,
            quantity,
            "node",
            EligibleFromCandleIndex: 0,
            OrderStatus.PendingSubmit);
    }

    private static OrderSubmitInternal Submit(OrderSnapshot order)
    {
        return new OrderSubmitInternal(new BotId(Bot), new ClientOrderId("pg-1"), order, MustLookUpBeforeSubmit: false);
    }
}